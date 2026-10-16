using System.Security.Cryptography;
using System.Text.Json;
using iTerminal.Core.Data;
using iTerminal.Core.Model.Passwords;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Services;

/// <summary>
/// <see cref="IPasswordManager"/> in its own SQLite file, <c>passwords.sqlite</c>, next to the main database. It is
/// not in the main database because that one is recreated whenever the app's data model changes, and a recreation
/// would take every saved password with it. This file has no EF model: two tables, written with plain SQL.
/// <c>PasswordManagerMeta</c> holds the generated key, sealed under the app's Data Protection keys (on Windows
/// those are protected by the user's DPAPI), and a verifier (a known phrase sealed under the key, so a key that
/// does not belong to the file is recognised without opening an entry); <c>PasswordEntries</c> holds one sealed
/// JSON document per entry. While unlocked, the decrypted entries are kept in memory, so a lookup never touches the
/// file or the Data Protection keys.
/// </summary>
public sealed class PasswordManager : IPasswordManager
{
    private const string Verifier = "iTerminal password manager";
    private const string MetaKey = "key";
    private const string MetaVerifier = "verifier";
    private const string MetaRemembered = "remembered";
    private const string MailPrefix = "mail:";

    private readonly string _databasePath;
    private readonly string _legacyKeyDirectory;
    private readonly IDbContextFactory<iTerminalDbContext>? _legacyVault;
    private readonly Lock _lock = new();

    private byte[]? _key;
    private Dictionary<string, PasswordEntry>? _entries;

    /// <param name="databasePath">The password file. Created on first use.</param>
    /// <param name="keyDirectory">The Data Protection keys the generated key is sealed with. The old vault was sealed with them too, and is read once on the first unlock.</param>
    /// <param name="legacyVault">The main database, where the old vault's rows are; null when there are none to bring across.</param>
    public PasswordManager(string databasePath, string keyDirectory, IDbContextFactory<iTerminalDbContext>? legacyVault = null)
    {
        _databasePath = databasePath;
        _legacyKeyDirectory = keyDirectory;
        _legacyVault = legacyVault;
        using var connection = Open();
    }

    public PasswordManager()
        : this(Path.Combine(Path.GetTempPath(), $"iTerminal.Passwords.{Guid.NewGuid():N}.sqlite"),
            Path.Combine(Path.GetTempPath(), $"iTerminal.PasswordKeys.{Guid.NewGuid():N}"))
    {
    }

    public bool IsUnlocked
    {
        get
        {
            lock (_lock)
            {
                return _key is not null;
            }
        }
    }

    public bool Unlock()
    {
        lock (_lock)
        {
            if (_key is not null)
            {
                return true;
            }

            var connection = Open();
            try
            {
                var meta = ReadMeta(connection);
                if (meta.ContainsKey(MetaKey))
                {
                    return TryOpenGeneratedKey(connection, meta);
                }

                if (meta.ContainsKey(MetaVerifier) && TryAdoptRememberedKey(connection, meta))
                {
                    return true;
                }

                if (meta.ContainsKey(MetaVerifier))
                {
                    // Sealed under a typed master password that nothing remembers: set the file aside and start again.
                    connection.Dispose();
                    SqliteConnection.ClearAllPools();
                    File.Move(_databasePath, $"{_databasePath}.master-password.bak", overwrite: true);
                    connection = Open();
                }

                CreateStore(connection);
                return true;
            }
            finally
            {
                connection.Dispose();
            }
        }
    }

    public void Lock()
    {
        lock (_lock)
        {
            if (_key is not null)
            {
                CryptographicOperations.ZeroMemory(_key);
            }

            _key = null;
            _entries = null;
        }
    }

    /// <summary>A new store: a random key, sealed on this machine, with the old vault's passwords brought across.</summary>
    private void CreateStore(SqliteConnection connection)
    {
        var key = PasswordCrypto.NewKey();
        var entries = new Dictionary<string, PasswordEntry>(StringComparer.Ordinal);
        foreach (var legacy in ReadLegacyVault())
        {
            entries[legacy.Id] = legacy;
        }

        using (var transaction = connection.BeginTransaction())
        {
            WriteMeta(connection, transaction, key);
            foreach (var entry in entries.Values)
            {
                WriteEntry(connection, transaction, key, entry);
            }

            transaction.Commit();
        }

        DeleteLegacyRows(entries.Keys);
        _key = key;
        _entries = entries;
    }

    private bool TryOpenGeneratedKey(SqliteConnection connection, Dictionary<string, string> meta)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(KeyProtector().Unprotect(meta[MetaKey]));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // The Data Protection keys are gone (another user, a wiped profile): the file cannot be opened again.
            return false;
        }

        return TryUse(connection, meta, key);
    }

    /// <summary>
    /// A store set up under a master password whose key was remembered keeps working: that key is 32 random bytes
    /// like a generated one, so it becomes the permanent key. Without it the entries cannot be read.
    /// </summary>
    private bool TryAdoptRememberedKey(SqliteConnection connection, Dictionary<string, string> meta)
    {
        if (!meta.TryGetValue(MetaRemembered, out var sealedValue))
        {
            return false;
        }

        byte[] key;
        try
        {
            var remembered = JsonSerializer.Deserialize<RememberedKey>(KeyProtector("iTerminal.PasswordManager.Remember.v1").Unprotect(sealedValue));
            if (remembered is null || remembered.Until <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            key = Convert.FromBase64String(remembered.Key);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return false;
        }

        if (PasswordCrypto.Open(key, meta[MetaVerifier]) != Verifier)
        {
            CryptographicOperations.ZeroMemory(key);
            return false;
        }

        using (var transaction = connection.BeginTransaction())
        {
            WriteMeta(connection, transaction, key);
            transaction.Commit();
        }

        _key = key;
        _entries = ReadEntries(connection, key);
        return true;
    }

    private bool TryUse(SqliteConnection connection, Dictionary<string, string> meta, byte[] key)
    {
        if (!meta.TryGetValue(MetaVerifier, out var verifier) || PasswordCrypto.Open(key, verifier) != Verifier)
        {
            CryptographicOperations.ZeroMemory(key);
            return false;
        }

        _key = key;
        _entries = ReadEntries(connection, key);
        return true;
    }

    /// <summary>
    /// Seals the key under the app's Data Protection keys — the same ones the old vault used, under their own
    /// purpose. On Windows those keys are protected by the user's DPAPI; elsewhere they are only as private as the
    /// folder they are kept in.
    /// </summary>
    private IDataProtector KeyProtector(string purpose = "iTerminal.PasswordManager.Key.v1") =>
        DataProtectionProvider.Create(
            new DirectoryInfo(_legacyKeyDirectory),
            builder => builder.SetApplicationName("iTerminal"))
            .CreateProtector(purpose);

    private sealed record RememberedKey(string Key, DateTimeOffset Until);

    public IReadOnlyList<PasswordEntry> List()
    {
        lock (_lock)
        {
            return _entries is null
                ? []
                : _entries.Values.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        }
    }

    public PasswordEntry? Get(string id)
    {
        lock (_lock)
        {
            return _entries is not null && _entries.TryGetValue(id, out var entry) ? entry : null;
        }
    }

    public void Save(PasswordEntry entry)
    {
        lock (_lock)
        {
            RequireUnlocked();
            var stored = entry with { Updated = DateTimeOffset.UtcNow };
            using var connection = Open();
            WriteEntry(connection, null, _key!, stored);
            _entries![stored.Id] = stored;
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            RequireUnlocked();
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM PasswordEntries WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
            _entries!.Remove(id);
        }
    }

    public string? GetSecret(string profileId)
    {
        lock (_lock)
        {
            return _entries is not null && _entries.TryGetValue(profileId, out var entry) ? entry.Password : null;
        }
    }

    public void SaveSecret(string profileId, string secret)
    {
        lock (_lock)
        {
            RequireUnlocked();
            var existing = _entries!.GetValueOrDefault(profileId);
            Save(existing is null
                ? new PasswordEntry
                {
                    Id = profileId,
                    Title = "Saved login",
                    Category = profileId.StartsWith(MailPrefix, StringComparison.Ordinal) ? PasswordCategory.Mail : PasswordCategory.Network,
                    Password = secret,
                }
                : existing with { Password = secret });
        }
    }

    public void DeleteSecret(string profileId) => Delete(profileId);

    private void RequireUnlocked()
    {
        if (_key is null || _entries is null)
        {
            throw new InvalidOperationException("The password manager is locked. Unlock it to change saved passwords.");
        }
    }

    private static Dictionary<string, PasswordEntry> ReadEntries(SqliteConnection connection, byte[] key)
    {
        var entries = new Dictionary<string, PasswordEntry>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Blob FROM PasswordEntries";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var json = PasswordCrypto.Open(key, reader.GetString(1));
            if (json is null)
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<PasswordEntry>(json);
            if (entry is not null)
            {
                entries[entry.Id] = entry;
            }
        }

        return entries;
    }

    private static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM PasswordManagerMeta";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            meta[reader.GetString(0)] = reader.GetString(1);
        }

        return meta;
    }

    private void WriteMeta(SqliteConnection connection, SqliteTransaction transaction, byte[] key)
    {
        Run(connection, transaction, "DELETE FROM PasswordManagerMeta");
        Run(connection, transaction, "INSERT INTO PasswordManagerMeta (Key, Value) VALUES ($k, $v)", ("$k", MetaKey), ("$v", KeyProtector().Protect(Convert.ToBase64String(key))));
        Run(connection, transaction, "INSERT INTO PasswordManagerMeta (Key, Value) VALUES ($k, $v)", ("$k", MetaVerifier), ("$v", PasswordCrypto.Seal(key, Verifier)));
    }

    private static void WriteEntry(SqliteConnection connection, SqliteTransaction? transaction, byte[] key, PasswordEntry entry) =>
        Run(connection, transaction,
            "INSERT OR REPLACE INTO PasswordEntries (Id, Blob) VALUES ($id, $blob)",
            ("$id", entry.Id),
            ("$blob", PasswordCrypto.Seal(key, JsonSerializer.Serialize(entry))));

    private static void Run(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    /// <summary>The passwords the old Data Protection vault holds, each as an entry. Unreadable rows are left alone.</summary>
    private List<PasswordEntry> ReadLegacyVault()
    {
        var migrated = new List<PasswordEntry>();
        if (_legacyVault is null || !Directory.Exists(_legacyKeyDirectory))
        {
            return migrated;
        }

        var protector = DataProtectionProvider.Create(
            new DirectoryInfo(_legacyKeyDirectory),
            builder => builder.SetApplicationName("iTerminal"))
            .CreateProtector("iTerminal.Vault.v1");

        using var context = _legacyVault.CreateDbContext();
        foreach (var row in context.VaultEntries.AsNoTracking().ToList())
        {
            string password;
            try
            {
                password = protector.Unprotect(row.EncryptedSecret);
            }
            catch (CryptographicException)
            {
                continue;
            }

            var isMail = row.ProfileId.StartsWith(MailPrefix, StringComparison.Ordinal);
            migrated.Add(new PasswordEntry
            {
                Id = row.ProfileId,
                Title = isMail ? "Mail login (moved)" : "Saved login (moved)",
                Category = isMail ? PasswordCategory.Mail : PasswordCategory.Network,
                Password = password,
                Updated = DateTimeOffset.UtcNow,
            });
        }

        return migrated;
    }

    private void DeleteLegacyRows(IEnumerable<string> migratedIds)
    {
        if (_legacyVault is null)
        {
            return;
        }

        using var context = _legacyVault.CreateDbContext();
        foreach (var id in migratedIds)
        {
            context.Database.ExecuteSqlRaw("DELETE FROM \"VaultEntries\" WHERE \"ProfileId\" = {0}", id);
        }
    }

    /// <summary>Opens the file, creating its tables on first use. Run on every open, so a file deleted under the app comes back empty rather than failing.</summary>
    private SqliteConnection Open()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        Run(connection, null, "CREATE TABLE IF NOT EXISTS PasswordManagerMeta (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL)");
        Run(connection, null, "CREATE TABLE IF NOT EXISTS PasswordEntries (Id TEXT NOT NULL PRIMARY KEY, Blob TEXT NOT NULL)");
        return connection;
    }
}
