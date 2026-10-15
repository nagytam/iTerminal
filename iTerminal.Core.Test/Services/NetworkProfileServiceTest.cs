using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class NetworkProfileServiceTest
{
    private static NetworkProfileService CreateService() => new(new SettingsService());

    [TestMethod]
    public void GetAll_WhenEmpty_ReturnsEmptyList()
    {
        var service = CreateService();

        Assert.AreEqual(0, service.GetAll().Count);
    }

    [TestMethod]
    public void Save_ThenGetAll_ContainsProfile()
    {
        var service = CreateService();
        var profile = new NetworkConnectionProfile { Host = "myhost", Username = "alice", Protocol = NetworkProtocol.Ssh };

        service.Save(profile);

        var all = service.GetAll();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("myhost", all[0].Host);
        Assert.AreEqual("alice", all[0].Username);
        Assert.AreEqual(NetworkProtocol.Ssh, all[0].Protocol);
    }

    [TestMethod]
    public void Save_TwiceWithSameId_Overwrites()
    {
        var service = CreateService();
        var profile = new NetworkConnectionProfile { Host = "myhost", Username = "alice" };

        service.Save(profile);
        profile.Username = "bob";
        service.Save(profile);

        var all = service.GetAll();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("bob", all[0].Username);
    }

    [TestMethod]
    public void Save_DistinctProfiles_KeepsBoth()
    {
        var service = CreateService();

        service.Save(new NetworkConnectionProfile { Host = "host1" });
        service.Save(new NetworkConnectionProfile { Host = "host2" });

        Assert.AreEqual(2, service.GetAll().Count);
    }

    [TestMethod]
    public void Get_ExistingId_ReturnsProfile()
    {
        var service = CreateService();
        var profile = new NetworkConnectionProfile { Host = "myhost" };
        service.Save(profile);

        var found = service.Get(profile.Id);

        Assert.IsNotNull(found);
        Assert.AreEqual("myhost", found!.Host);
    }

    [TestMethod]
    public void Get_UnknownId_ReturnsNull()
    {
        var service = CreateService();

        Assert.IsNull(service.Get("does-not-exist"));
    }

    [TestMethod]
    public void Delete_RemovesProfile()
    {
        var service = CreateService();
        var profile = new NetworkConnectionProfile { Host = "myhost" };
        service.Save(profile);

        service.Delete(profile.Id);

        Assert.AreEqual(0, service.GetAll().Count);
    }

    [TestMethod]
    public void Delete_UnknownId_DoesNotThrow()
    {
        var service = CreateService();

        service.Delete("does-not-exist");
    }

    [TestMethod]
    public void SmbProfile_RoundTripsShareNameAndProtocol()
    {
        var service = CreateService();
        var profile = new NetworkConnectionProfile
        {
            Host = "fileserver",
            Protocol = NetworkProtocol.Smb,
            ShareName = "Public",
            Username = "guest",
        };

        service.Save(profile);

        var found = service.Get(profile.Id);
        Assert.IsNotNull(found);
        Assert.AreEqual(NetworkProtocol.Smb, found!.Protocol);
        Assert.AreEqual("Public", found.ShareName);
    }

    [TestMethod]
    public void SshProfile_RoundTripsAuthMethodAndKeyPath()
    {
        var service = CreateService();
        var profile = new NetworkConnectionProfile
        {
            Host = "myhost",
            Protocol = NetworkProtocol.Ssh,
            SshAuthMethod = SshAuthMethod.PrivateKey,
            PrivateKeyPath = "/home/alice/.ssh/id_ed25519",
        };

        service.Save(profile);

        var found = service.Get(profile.Id);
        Assert.IsNotNull(found);
        Assert.AreEqual(SshAuthMethod.PrivateKey, found!.SshAuthMethod);
        Assert.AreEqual("/home/alice/.ssh/id_ed25519", found.PrivateKeyPath);
    }

    [TestMethod]
    public void Profiles_PersistAcrossServiceInstances_WithSharedSettingsService()
    {
        var settingsService = new SettingsService();
        var first = new NetworkProfileService(settingsService);
        first.Save(new NetworkConnectionProfile { Host = "myhost" });

        var second = new NetworkProfileService(settingsService);

        Assert.AreEqual(1, second.GetAll().Count);
    }
}
