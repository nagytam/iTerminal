using iTerminal.Core.Data;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class PasswordManagerTest
{
    private const string Master = "correct horse battery";

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
    public void SetupCreatesTheStoreAndLeavesItUnlocked()
    {
        var manager = Open(NewStore());

        Assert.IsFalse(manager.IsSetUp);

        manager.Setup(Master);

        Assert.IsTrue(manager.IsSetUp);
        Assert.IsTrue(manager.IsUnlocked);
    }

    [TestMethod]
    public void AShortMasterPasswordIsRefused()
    {
        var manager = Open(NewStore());

        Throws<ArgumentException>(() => manager.Setup("short"));
        Assert.IsFalse(manager.IsSetUp);
    }

    [TestMethod]
    public void AWrongMasterPasswordDoesNotUnlock()
    {
        var manager = Open(NewStore());
        manager.Setup(Master);
        manager.Lock();

        Assert.IsFalse(manager.Unlock("not the master password"));
        Assert.IsFalse(manager.IsUnlocked);
        Assert.IsTrue(manager.Unlock(Master));
    }

    [TestMethod]
    public void WhileLockedNothingIsReadAndNothingIsWritten()
    {
        var manager = Open(NewStore());
        manager.Setup(Master);
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
        first.Setup(Master);
        first.Save(new PasswordEntry { Id = "ssh-1", Title = "SSH ada@host", Username = "ada", Category = PasswordCategory.Network, Password = "hunter2!" });

        var reopened = Open(store);
        Assert.IsTrue(reopened.IsSetUp);
        Assert.IsFalse(reopened.IsUnlocked);
        Assert.IsTrue(reopened.Unlock(Master));

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
        manager.Setup(Master);
        manager.Save(new PasswordEntry { Id = "smb-1", Title = "SMB ada@files/photos", Password = "old" });

        manager.SaveSecret("smb-1", "new");

        Assert.AreEqual("SMB ada@files/photos", manager.Get("smb-1")!.Title);
        Assert.AreEqual("new", manager.GetSecret("smb-1"));
    }

    [TestMethod]
    public void MailSecretsAreCategorisedAsMail()
    {
        var manager = Open(NewStore());
        manager.Setup(Master);

        manager.SaveSecret("mail:abc", "pw");

        Assert.AreEqual(PasswordCategory.Mail, manager.Get("mail:abc")!.Category);
    }

    [TestMethod]
    public void DeletingAnEntryForgetsIt()
    {
        var manager = Open(NewStore());
        manager.Setup(Master);
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
        manager.Setup(Master);
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
    public void ChangingTheMasterPasswordResealsEverything()
    {
        var store = NewStore();
        var manager = Open(store);
        manager.Setup(Master);
        manager.SaveSecret("ssh-1", "hunter2!");

        manager.ChangeMasterPassword(Master, "a brand new master");
        manager.Lock();

        Assert.IsFalse(manager.Unlock(Master));
        Assert.IsTrue(manager.Unlock("a brand new master"));
        Assert.AreEqual("hunter2!", manager.GetSecret("ssh-1"));
    }

    [TestMethod]
    public void ChangingTheMasterPasswordNeedsTheCurrentOne()
    {
        var manager = Open(NewStore());
        manager.Setup(Master);

        Throws<UnauthorizedAccessException>(() => manager.ChangeMasterPassword("wrong current", "a brand new master"));
    }

    [TestMethod]
    public void TheOldVaultsSecretsAreBroughtAcrossAtSetup()
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
        manager.Setup(Master);

        Assert.AreEqual("saved before the password manager", manager.GetSecret("legacy-profile"));
        using var check = mainFactory.CreateDbContext();
        Assert.AreEqual(0, check.VaultEntries.Count());
    }

    [TestMethod]
    public void ARememberedKeyUnlocksAfterReopening()
    {
        var store = NewStore();
        var first = Open(store);
        first.Setup(Master);
        first.Save(new PasswordEntry { Id = "ssh:box", Title = "Box", Password = "hunter2" });
        first.Remember(TimeSpan.FromDays(30));

        var later = Open(store);
        Assert.IsTrue(later.UnlockRemembered());
        Assert.AreEqual("hunter2", later.GetSecret("ssh:box"));
    }

    [TestMethod]
    public void AnExpiredRememberedKeyIsForgottenAndTheMasterPasswordIsAskedFor()
    {
        var store = NewStore();
        var first = Open(store);
        first.Setup(Master);
        first.Remember(TimeSpan.Zero);

        var later = Open(store);
        Assert.IsFalse(later.UnlockRemembered());
        Assert.IsFalse(later.IsUnlocked);
        Assert.IsFalse(Open(store).UnlockRemembered());
        Assert.IsTrue(later.Unlock(Master));
    }

    [TestMethod]
    public void LockingForgetsTheRememberedKey()
    {
        var store = NewStore();
        var first = Open(store);
        first.Setup(Master);
        first.Remember(TimeSpan.FromDays(30));
        first.Lock();

        Assert.IsFalse(Open(store).UnlockRemembered());
    }

    [TestMethod]
    public void WhileLockedThereIsNothingToRemember()
    {
        var manager = Open(NewStore());
        manager.Setup(Master);
        manager.Lock();

        Throws<InvalidOperationException>(() => manager.Remember(TimeSpan.FromDays(30)));
    }
}
