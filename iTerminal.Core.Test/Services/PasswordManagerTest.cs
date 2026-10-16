using iTerminal.Core.Data;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class PasswordManagerTest
{
    private sealed class SqliteDbContextFactory(string databasePath) : IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options = new DbContextOptionsBuilder<iTerminalDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        public iTerminalDbContext CreateDbContext() => new(_options);

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class Store
    {
        public required string DatabasePath { get; init; }
        public required string KeyDirectory { get; init; }
    }

    /// <summary>The exception the action throws, which must be of type <typeparamref name="T"/>.</summary>
    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        Assert.Fail($"Expected {typeof(T).Name}.");
    }

    private static Store NewStore() => new()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"iTerminal.PasswordTest.{Guid.NewGuid():N}.sqlite"),
        KeyDirectory = Path.Combine(Path.GetTempPath(), $"iTerminal.PasswordKeys.{Guid.NewGuid():N}"),
    };

    private static PasswordManager Open(Store store) => new(store.DatabasePath, store.KeyDirectory);

    [TestMethod]
    public void TheFirstUnlockCreatesTheStoreAndLeavesItUnlocked()
    {
        var manager = Open(NewStore());

        Assert.IsFalse(manager.IsUnlocked);
        Assert.IsTrue(manager.Unlock());
        Assert.IsTrue(manager.IsUnlocked);
    }

    [TestMethod]
    public void ALockedStoreUnlocksAgainWithoutAnyPassword()
    {
        var manager = Open(NewStore());
        manager.Unlock();
        manager.SaveSecret("ssh-1", "hunter2!");
        manager.Lock();

        Assert.IsFalse(manager.IsUnlocked);
        Assert.IsTrue(manager.Unlock());
        Assert.AreEqual("hunter2!", manager.GetSecret("ssh-1"));
    }

    [TestMethod]
    public void WhileLockedNothingIsReadAndNothingIsWritten()
    {
        var manager = Open(NewStore());
        manager.Unlock();
        manager.SaveSecret("ssh-1", "hunter2!");
        manager.Lock();

        Assert.IsNull(manager.GetSecret("ssh-1"));
        Assert.AreEqual(0, manager.List().Count);
        Throws<InvalidOperationException>(() => manager.SaveSecret("ssh-2", "x"));
    }

    [TestMethod]
    public void SavedPasswordsAreThereAfterReopeningAndUnlocking()
    {
        var store = NewStore();
        var first = Open(store);
        first.Unlock();
        first.Save(new PasswordEntry { Id = "ssh-1", Title = "SSH ada@host", Username = "ada", Category = PasswordCategory.Network, Password = "hunter2!" });

        var reopened = Open(store);
        Assert.IsFalse(reopened.IsUnlocked);
        Assert.IsTrue(reopened.Unlock());

        var entry = reopened.Get("ssh-1");
        Assert.IsNotNull(entry);
        Assert.AreEqual("SSH ada@host", entry.Title);
        Assert.AreEqual("hunter2!", entry.Password);
        Assert.AreEqual(PasswordCategory.Network, entry.Category);
    }

    [TestMethod]
    public void SaveSecretKeepsTheTitleOfAnEntryThatAlreadyHasOne()
    {
        var manager = Open(NewStore());
        manager.Unlock();
        manager.Save(new PasswordEntry { Id = "smb-1", Title = "SMB ada@files/photos", Password = "old" });

        manager.SaveSecret("smb-1", "new");

        Assert.AreEqual("SMB ada@files/photos", manager.Get("smb-1")!.Title);
        Assert.AreEqual("new", manager.GetSecret("smb-1"));
    }

    [TestMethod]
    public void MailSecretsAreCategorisedAsMail()
    {
        var manager = Open(NewStore());
        manager.Unlock();

        manager.SaveSecret("mail:abc", "pw");

        Assert.AreEqual(PasswordCategory.Mail, manager.Get("mail:abc")!.Category);
    }

    [TestMethod]
    public void DeletingAnEntryForgetsIt()
    {
        var manager = Open(NewStore());
        manager.Unlock();
        manager.SaveSecret("gone", "pw");

        manager.DeleteSecret("gone");

        Assert.IsNull(manager.GetSecret("gone"));
        Assert.AreEqual(0, manager.List().Count);
    }

    [TestMethod]
    public void ThePasswordIsNotStoredAsPlainTextOnDisk()
    {
        var store = NewStore();
        var manager = Open(store);
        manager.Unlock();
        manager.SaveSecret("ssh-1", "plaintext-canary-value");

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Blob FROM PasswordEntries";
        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read());
        Assert.IsFalse(reader.GetString(0).Contains("plaintext-canary-value"));
    }

    [TestMethod]
    public void TheOldVaultsSecretsAreBroughtAcrossOnTheFirstUnlock()
    {
        var store = NewStore();
        var mainDatabase = Path.Combine(Path.GetTempPath(), $"iTerminal.MainDb.{Guid.NewGuid():N}.sqlite");
        var mainFactory = new SqliteDbContextFactory(mainDatabase);
        var protector = DataProtectionProvider.Create(new DirectoryInfo(store.KeyDirectory), builder => builder.SetApplicationName("iTerminal"))
            .CreateProtector("iTerminal.Vault.v1");
        var legacy = protector.Protect("saved before the password manager");
        using (var context = mainFactory.CreateDbContext())
        {
            context.Database.EnsureCreated();
            context.Database.ExecuteSqlRaw("INSERT INTO \"VaultEntries\" (\"ProfileId\", \"EncryptedSecret\") VALUES ({0}, {1})", "legacy-profile", legacy);
        }

        var manager = new PasswordManager(store.DatabasePath, store.KeyDirectory, mainFactory);
        manager.Unlock();

        Assert.AreEqual("saved before the password manager", manager.GetSecret("legacy-profile"));
        using var check = mainFactory.CreateDbContext();
        Assert.AreEqual(0, check.VaultEntries.Count());
    }

    [TestMethod]
    public void TheKeyIsSealedOnDiskNotStoredRaw()
    {
        var store = NewStore();
        Open(store).Unlock();

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM PasswordManagerMeta WHERE Key = 'key'";
        var sealedKey = (string?)command.ExecuteScalar();
        Assert.IsNotNull(sealedKey);
        // A raw 32-byte key is 44 base64 characters; the sealed one carries the Data Protection envelope.
        Assert.IsTrue(sealedKey.Length > 44);
    }

    [TestMethod]
    public void AStoreWithoutItsDataProtectionKeysStaysLocked()
    {
        var store = NewStore();
        Open(store).Unlock();

        var elsewhere = new PasswordManager(store.DatabasePath, Path.Combine(Path.GetTempPath(), $"iTerminal.PasswordKeys.{Guid.NewGuid():N}"));

        Assert.IsFalse(elsewhere.Unlock());
        Assert.IsFalse(elsewhere.IsUnlocked);
    }

    [TestMethod]
    public void ARememberedMasterPasswordKeyBecomesThePermanentKey()
    {
        var store = NewStore();
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var protector = DataProtectionProvider.Create(new DirectoryInfo(store.KeyDirectory), builder => builder.SetApplicationName("iTerminal"));
        var remembered = System.Text.Json.JsonSerializer.Serialize(new { Key = Convert.ToBase64String(key), Until = DateTimeOffset.UtcNow.AddDays(1) });
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath}"))
        {
            connection.Open();
            Exec(connection, "CREATE TABLE PasswordManagerMeta (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL)");
            Exec(connection, "CREATE TABLE PasswordEntries (Id TEXT NOT NULL PRIMARY KEY, Blob TEXT NOT NULL)");
            Exec(connection, "INSERT INTO PasswordManagerMeta VALUES ('verifier', $v)", ("$v", PasswordCrypto.Seal(key, "iTerminal password manager")));
            Exec(connection, "INSERT INTO PasswordManagerMeta VALUES ('remembered', $v)", ("$v", protector.CreateProtector("iTerminal.PasswordManager.Remember.v1").Protect(remembered)));
            Exec(connection, "INSERT INTO PasswordEntries VALUES ('ssh:box', $b)", ("$b", PasswordCrypto.Seal(key, System.Text.Json.JsonSerializer.Serialize(new PasswordEntry { Id = "ssh:box", Title = "Box", Password = "hunter2" }))));
        }

        Assert.IsTrue(Open(store).Unlock());

        var later = Open(store);
        Assert.IsTrue(later.Unlock());
        Assert.AreEqual("hunter2", later.GetSecret("ssh:box"));
    }

    [TestMethod]
    public void AMasterPasswordStoreNothingRemembersIsSetAsideAndReplaced()
    {
        var store = NewStore();
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath}"))
        {
            connection.Open();
            Exec(connection, "CREATE TABLE PasswordManagerMeta (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL)");
            Exec(connection, "CREATE TABLE PasswordEntries (Id TEXT NOT NULL PRIMARY KEY, Blob TEXT NOT NULL)");
            Exec(connection, "INSERT INTO PasswordManagerMeta VALUES ('verifier', $v)", ("$v", PasswordCrypto.Seal(key, "iTerminal password manager")));
        }

        var manager = Open(store);

        Assert.IsTrue(manager.Unlock());
        Assert.IsTrue(File.Exists($"{store.DatabasePath}.master-password.bak"));
        manager.SaveSecret("ssh-1", "x");
        var later = Open(store);
        Assert.IsTrue(later.Unlock());
        Assert.AreEqual("x", later.GetSecret("ssh-1"));
    }

    private static void Exec(Microsoft.Data.Sqlite.SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
