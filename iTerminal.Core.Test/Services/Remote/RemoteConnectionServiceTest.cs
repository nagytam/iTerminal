using iTerminal.Core.Model.Remote;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Remote;

namespace iTerminal.Core.Test.Services.Remote;

[TestClass]
public class RemoteConnectionServiceTest
{
    private static RemoteConnectionService CreateService(SettingsService? settings = null) => new(settings ?? new SettingsService());

    [TestMethod]
    public void NothingIsSavedAtFirst()
    {
        Assert.AreEqual(0, CreateService().GetAll().Count);
    }

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

    [TestMethod]
    public void TheListIsInNameOrder()
    {
        var service = CreateService();
        service.Save(new RemoteConnection { Name = "zulu", Host = "z" });
        service.Save(new RemoteConnection { Name = "Alpha", Host = "a" });

        CollectionAssert.AreEqual(new[] { "Alpha", "zulu" }, service.GetAll().Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void AnUnsoundConnectionIsNotSaved()
    {
        var service = CreateService();

        Assert.ThrowsExactly<InvalidOperationException>(() => service.Save(new RemoteConnection { Name = "bad", Host = "-oProxyCommand=x" }));
        Assert.AreEqual(0, service.GetAll().Count);
    }

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
