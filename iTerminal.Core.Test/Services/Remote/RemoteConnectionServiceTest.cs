using iTerminal.Core.Model.Remote;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Remote;

namespace iTerminal.Core.Test.Services.Remote;

[TestClass]
public class RemoteConnectionServiceTest
{
    private static RemoteConnectionService CreateService(SettingsService? settings = null) => new(settings ?? new SettingsService());

    private static RemoteConnection FtpEntry() => new()
    {
        Name = "files", Protocol = RemoteProtocol.Ftp, Host = "ftp.example.com", Port = 2121, Username = "alice",
    };

    [Timeout(60_000)]
    [TestMethod]
    public void AnFtpEntryRecordsTheLoginTheDriveLooksFor()
    {
        var settings = new SettingsService();
        var profiles = new NetworkProfileService(settings);
        var passwords = new Moq.Mock<IPasswordManager>();
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        var service = new RemoteConnectionService(settings, profiles, passwords.Object);
        var entry = FtpEntry();

        service.Save(entry, "s3cret");

        var profile = profiles.Get(entry.Id)!;
        Assert.AreEqual(iTerminal.Core.Model.Network.NetworkProtocol.Ftp, profile.Protocol);
        Assert.AreEqual("ftp.example.com:2121", profile.Host); // as Url.Host reads it: the port rides along
        Assert.AreEqual("alice", profile.Username);
        Assert.IsTrue(profile.HasSavedCredential);
        Assert.IsTrue(service.Get(entry.Id)!.HasSavedCredential);
        passwords.Verify(p => p.Save(Moq.It.Is<iTerminal.Core.Model.Passwords.PasswordEntry>(e => e.Id == entry.Id && e.Password == "s3cret")), Moq.Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASecondSaveWithoutAPasswordKeepsTheOneSaved()
    {
        var settings = new SettingsService();
        var passwords = new Moq.Mock<IPasswordManager>();
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        var service = new RemoteConnectionService(settings, new NetworkProfileService(settings), passwords.Object);
        var entry = FtpEntry();
        service.Save(entry, "s3cret");

        var edited = service.Get(entry.Id)!;
        edited.Notes = "renamed";
        service.Save(edited, string.Empty);

        Assert.IsTrue(service.Get(entry.Id)!.HasSavedCredential);
        passwords.Verify(p => p.Save(Moq.It.IsAny<iTerminal.Core.Model.Passwords.PasswordEntry>()), Moq.Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALockedPasswordManagerRefusesTheSaveAndNothingIsRecorded()
    {
        var settings = new SettingsService();
        var profiles = new NetworkProfileService(settings);
        var passwords = new Moq.Mock<IPasswordManager>();
        passwords.SetupGet(p => p.IsUnlocked).Returns(false);
        passwords.Setup(p => p.Unlock()).Returns(false);
        var service = new RemoteConnectionService(settings, profiles, passwords.Object);

        Assert.ThrowsExactly<InvalidOperationException>(() => service.Save(FtpEntry(), "s3cret"));

        Assert.AreEqual(0, service.GetAll().Count);
        Assert.AreEqual(0, profiles.GetAll().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeletingAnFtpEntryRemovesItsProfileAndPassword()
    {
        var settings = new SettingsService();
        var profiles = new NetworkProfileService(settings);
        var passwords = new Moq.Mock<IPasswordManager>();
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        var service = new RemoteConnectionService(settings, profiles, passwords.Object);
        var entry = FtpEntry();
        service.Save(entry, "s3cret");

        service.Delete(entry.Id);

        Assert.AreEqual(0, profiles.GetAll().Count);
        passwords.Verify(p => p.DeleteSecret(entry.Id), Moq.Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ChangingAnFtpEntryIntoSshDropsItsLogin()
    {
        var settings = new SettingsService();
        var profiles = new NetworkProfileService(settings);
        var passwords = new Moq.Mock<IPasswordManager>();
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        var service = new RemoteConnectionService(settings, profiles, passwords.Object);
        var entry = FtpEntry();
        service.Save(entry, "s3cret");

        var changed = service.Get(entry.Id)!;
        changed.Protocol = RemoteProtocol.Ssh;
        service.Save(changed, "ignored");

        Assert.AreEqual(0, profiles.GetAll().Count);
        Assert.IsFalse(service.Get(entry.Id)!.HasSavedCredential);
        passwords.Verify(p => p.DeleteSecret(entry.Id), Moq.Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingIsSavedAtFirst()
    {
        Assert.AreEqual(0, CreateService().GetAll().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASavedConnectionComesBackWithAllItsFields()
    {
        var settings = new SettingsService();
        CreateService(settings).Save(new RemoteConnection
        {
            Name = "build box",
            Protocol = RemoteProtocol.PowerShell,
            Transport = PowerShellTransport.Ssh,
            Host = "build.example.com",
            Port = 2222,
            Username = "alice",
            PrivateKeyPath = "/keys/id",
            Notes = "ci",
        });

        // A second service over the same settings: it has to be read back from the stored JSON.
        var loaded = CreateService(settings).GetAll().Single();

        Assert.AreEqual("build box", loaded.Name);
        Assert.AreEqual(RemoteProtocol.PowerShell, loaded.Protocol);
        Assert.AreEqual(PowerShellTransport.Ssh, loaded.Transport);
        Assert.AreEqual("build.example.com", loaded.Host);
        Assert.AreEqual(2222, loaded.Port);
        Assert.AreEqual("alice", loaded.Username);
        Assert.AreEqual("/keys/id", loaded.PrivateKeyPath);
        Assert.AreEqual("ci", loaded.Notes);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SavingTheSameIdReplacesTheEntry()
    {
        var service = CreateService();
        var connection = new RemoteConnection { Name = "a", Host = "h1" };
        service.Save(connection);

        connection.Host = "h2";
        service.Save(connection);

        Assert.AreEqual("h2", service.GetAll().Single().Host);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheListIsInNameOrder()
    {
        var service = CreateService();
        service.Save(new RemoteConnection { Name = "zulu", Host = "z" });
        service.Save(new RemoteConnection { Name = "Alpha", Host = "a" });

        CollectionAssert.AreEqual(new[] { "Alpha", "zulu" }, service.GetAll().Select(c => c.Name).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnsoundConnectionIsNotSaved()
    {
        var service = CreateService();

        Assert.ThrowsExactly<InvalidOperationException>(() => service.Save(new RemoteConnection { Name = "bad", Host = "-oProxyCommand=x" }));
        Assert.AreEqual(0, service.GetAll().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeleteRemovesOnlyThatEntry()
    {
        var service = CreateService();
        var keep = new RemoteConnection { Name = "keep", Host = "k" };
        var drop = new RemoteConnection { Name = "drop", Host = "d" };
        service.Save(keep);
        service.Save(drop);

        service.Delete(drop.Id);

        Assert.AreEqual(keep.Id, service.GetAll().Single().Id);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkConnectedStampsTheEntry()
    {
        var service = CreateService();
        var connection = new RemoteConnection { Name = "a", Host = "h" };
        service.Save(connection);

        service.MarkConnected(connection.Id);

        Assert.IsNotNull(service.Get(connection.Id)!.LastConnectedUtc);
    }
}
