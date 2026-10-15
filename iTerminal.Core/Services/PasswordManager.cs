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
/// <c>PasswordManagerMeta</c> holds the salt, the iteration count and a verifier (a known phrase sealed under the
/// key, so a wrong master password is recognised without opening an entry); <c>PasswordEntries</c> holds one sealed
/// JSON document per entry. While unlocked, the decrypted entries are kept in memory, so a lookup never touches the
/// file or the key derivation.
/// </summary>
public sealed class PasswordManager : IPasswordManager
{
    public const int MinimumMasterPasswordLength = 8;

    private const string Verifier = "iTerminal password manager";
    private const string MetaSalt = "salt";
    private const string MetaIterations = "iterations";
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
    /// <param name="legacyKeyDirectory">The Data Protection keys the old vault was sealed with, read once at set-up.</param>
    /// <param name="legacyVault">The main database, where the old vault's rows are; null when there are none to bring across.</param>
    public PasswordManager(string databasePath, string legacyKeyDirectory, IDbContextFactory<iTerminalDbContext>? legacyVault = null)
    {
        _databasePath = databasePath;
        _legacyKeyDirectory = legacyKeyDirectory;
        _legacyVault = legacyVault;
        using var connection = Open();
    }

    public PasswordManager()
        : this(Path.Combine(Path.GetTempPath(), $"iTerminal.Passwords.{Guid.NewGuid():N}.sqlite"),
            Path.Combine(Path.GetTempPath(), $"iTerminal.PasswordKeys.{Guid.NewGuid():N}"))
    {
    }

    public bool IsSetUp
    {
        get
        {
            lock (_lock)
            {
                using var connection = Open();
                return ReadMeta(connection).ContainsKey(MetaVerifier);
            }
        }
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

    public void Setup(string masterPassword)
    {
        ValidateMasterPassword(masterPassword);
        lock (_lock)
        {
            using var connection = Open();
            if (ReadMeta(connection).ContainsKey(MetaVerifier))
            {
                throw new InvalidOperationException("The password manager is already set up.");
            }

            var salt = PasswordCrypto.NewSalt();
            var key = PasswordCrypto.DeriveKey(masterPassword, salt, PasswordCrypto.DefaultIterations);
            var entries = new Dictionary<string, PasswordEntry>(StringComparer.Ordinal);
            foreach (var legacy in ReadLegacyVault())
            {
                entries[legacy.Id] = legacy;
            }

            using (var transaction = connection.BeginTransaction())
            {
                WriteMeta(connection, transaction, salt, PasswordCrypto.DefaultIterations, PasswordCrypto.Seal(key, Verifier));
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
    }

    public bool Unlock(string masterPassword)
    {
        lock (_lock)
        {
            using var connection = Open();
            if (!TryDeriveKey(connection, masterPassword, out var key))
            {
                return false;
            }

            _key = key;
            _entries = ReadEntries(connection, key);
            return true;
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

            // A lock is a request to be asked again, so the remembered key goes too.
            using var connection = Open();
            ForgetRemembered(connection);
        }
    }

    public void Remember(TimeSpan lifetime)
    {
        lock (_lock)
        {
            if (_key is null)
            {
                throw new InvalidOperationException("Unlock the password manager before remembering it.");
            }

            var payload = JsonSerializer.Serialize(new RememberedKey(Convert.ToBase64String(_key), DateTimeOffset.UtcNow.Add(lifetime)));
            using var connection = Open();
            Run(connection, null, "INSERT OR REPLACE INTO PasswordManagerMeta (Key, Value) VALUES ($k, $v)",
                ("$k", MetaRemembered), ("$v", RememberProtector().Protect(payload)));
        }
    }

    public bool UnlockRemembered()
    {
        lock (_lock)
        {
            using var connection = Open();
            if (!ReadMeta(connection).TryGetValue(MetaRemembered, out var sealedValue))
            {
                return false;
            }

            RememberedKey? remembered;
            try
            {
                remembered = JsonSerializer.Deserialize<RememberedKey>(RememberProtector().Unprotect(sealedValue));
            }
            catch (CryptographicException)
            {
                remembered = null;
            }

            if (remembered is null || remembered.Until <= DateTimeOffset.UtcNow)
            {
                ForgetRemembered(connection);
                return false;
            }

            _key = Convert.FromBase64String(remembered.Key);
            _entries = ReadEntries(connection, _key);
            return true;
        }
    }

    private void ForgetRemembered(SqliteConnection connection) =>
        Run(connection, null, "DELETE FROM PasswordManagerMeta WHERE Key = $k", ("$k", MetaRemembered));

    /// <summary>
    /// Seals the remembered key under the app's Data Protection keys — the same ones the old vault used, under
    /// their own purpose. On Windows those keys are protected by the user's DPAPI; elsewhere they are only as
    /// private as the folder they are kept in.
    /// </summary>
    private IDataProtector RememberProtector() =>
        DataProtectionProvider.Create(
            new DirectoryInfo(_legacyKeyDirectory),
            builder => builder.SetApplicationName("iTerminal"))
            .CreateProtector("iTerminal.PasswordManager.Remember.v1");

    private sealed record RememberedKey(string Key, DateTimeOffset Until);

    public void ChangeMasterPassword(string currentPassword, string newPassword)
    {
        ValidateMasterPassword(newPassword);
        lock (_lock)
        {
            if (_key is null || _entries is null)
            {
                throw new InvalidOperationException("Unlock the password manager before changing its master password.");
            }

            using var connection = Open();
            if (!TryDeriveKey(connection, currentPassword, out var currentKey))
            {
                throw new UnauthorizedAccessException("The current master password is wrong.");
            }

            CryptographicOperations.ZeroMemory(currentKey);
            var salt = PasswordCrypto.NewSalt();
            var key = PasswordCrypto.DeriveKey(newPassword, salt, PasswordCrypto.DefaultIterations);
            using (var transaction = connection.BeginTransaction())
            {
                WriteMeta(connection, transaction, salt, PasswordCrypto.DefaultIterations, PasswordCrypto.Seal(key, Verifier));
                foreach (var entry in _entries.Values)
                {
                    WriteEntry(connection, transaction, key, entry);
                }

                transaction.Commit();
            }

            CryptographicOperations.ZeroMemory(_key);
            _key = key;
        }
    }

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

    private static void ValidateMasterPassword(string masterPassword)
    {
        if (masterPassword.Length < MinimumMasterPasswordLength)
        {
            throw new ArgumentException($"The master password needs at least {MinimumMasterPasswordLength} characters.", nameof(masterPassword));
        }
    }

    /// <summary>Derives the key for <paramref name="masterPassword"/> and checks it against the verifier.</summary>
    private static bool TryDeriveKey(SqliteConnection connection, string masterPassword, out byte[] key)
    {
        key = [];
        var meta = ReadMeta(connection);
        if (!meta.TryGetValue(MetaSalt, out var saltText)
            || !meta.TryGetValue(MetaIterations, out var iterationsText)
            || !meta.TryGetValue(MetaVerifier, out var verifier)
            || !int.TryParse(iterationsText, out var iterations))
        {
            return false;
        }

        var candidate = PasswordCrypto.DeriveKey(masterPassword, Convert.FromBase64String(saltText), iterations);
        if (PasswordCrypto.Open(candidate, verifier) != Verifier)
        {
            CryptographicOperations.ZeroMemory(candidate);
            return false;
        }

        key = candidate;
        return true;
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

    private static void WriteMeta(SqliteConnection connection, SqliteTransaction transaction, byte[] salt, int iterations, string verifier)
    {
        Run(connection, transaction, "DELETE FROM PasswordManagerMeta");
        Run(connection, transaction, "INSERT INTO PasswordManagerMeta (Key, Value) VALUES ($k, $v)", ("$k", MetaSalt), ("$v", Convert.ToBase64String(salt)));
        Run(connection, transaction, "INSERT INTO PasswordManagerMeta (Key, Value) VALUES ($k, $v)", ("$k", MetaIterations), ("$v", iterations.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Run(connection, transaction, "INSERT INTO PasswordManagerMeta (Key, Value) VALUES ($k, $v)", ("$k", MetaVerifier), ("$v", verifier));
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
