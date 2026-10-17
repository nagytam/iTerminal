using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class TrustedHostStoreTest
{
    private string _path = null!;

    [TestInitialize]
    public void Setup() => _path = Path.Combine(Path.GetTempPath(), $"iTerminal.KnownHosts.Test.{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [TestMethod]
    public void Check_RecordsANewHostThenTrustsIt()
    {
        var store = new TrustedHostStore(_path);

        Assert.AreEqual(HostTrust.New, store.Check("ssh:example.org:22", "ssh-ed25519 SHA256:abc", out _));
        Assert.AreEqual(HostTrust.Trusted, store.Check("ssh:example.org:22", "ssh-ed25519 SHA256:abc", out _));
    }

    [TestMethod]
    public void Check_RefusesAChangedKeyAndReportsTheRecordedOne()
    {
        var store = new TrustedHostStore(_path);
        store.Check("ssh:example.org:22", "ssh-ed25519 SHA256:abc", out _);

        var result = store.Check("ssh:example.org:22", "ssh-ed25519 SHA256:evil", out var recorded);

        Assert.AreEqual(HostTrust.Changed, result);
        Assert.AreEqual("ssh-ed25519 SHA256:abc", recorded);
        Assert.AreEqual(HostTrust.Changed, store.Check("ssh:example.org:22", "ssh-ed25519 SHA256:evil", out _), "a refusal must not record the new key");
    }

    [TestMethod]
    public void Check_RemembersAcrossInstances()
    {
        new TrustedHostStore(_path).Check("ftps:example.org:21", "SHA256:AA", out _);

        Assert.AreEqual(HostTrust.Trusted, new TrustedHostStore(_path).Check("FTPS:example.org:21", "SHA256:AA", out _));
    }

    [TestMethod]
    public void Forget_LetsTheNextKeyBeRecorded()
    {
        var store = new TrustedHostStore(_path);
        store.Check("ssh:example.org:22", "old", out _);

        Assert.IsTrue(store.Forget("ssh:example.org:22"));
        Assert.AreEqual(HostTrust.New, store.Check("ssh:example.org:22", "new", out _));
        Assert.IsFalse(store.Forget("ssh:other:22"));
    }

    [TestMethod]
    public void Check_TreatsAnUnreadableFileAsEmpty()
    {
        File.WriteAllText(_path, "not json");

        Assert.AreEqual(HostTrust.New, new TrustedHostStore(_path).Check("ssh:example.org:22", "k", out _));
    }
}
