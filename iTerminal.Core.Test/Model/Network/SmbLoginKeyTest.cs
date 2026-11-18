using iTerminal.Core.Model.Network;

namespace iTerminal.Core.Model.Network.Test;

[TestClass]
public class SmbLoginKeyTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Id_LowerCasesTheHostBecauseHostNamesIgnoreCase()
    {
        Assert.AreEqual("smb-login:nas.local\\Bob", SmbLoginKey.Id("NAS.Local", "Bob"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TryParse_ReadsBackTheHostAndUser()
    {
        var parsed = SmbLoginKey.TryParse(SmbLoginKey.Id("192.0.2.10", "alice"), out var host, out var username);

        Assert.IsTrue(parsed);
        Assert.AreEqual("192.0.2.10", host);
        Assert.AreEqual("alice", username);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TryParse_KeepsADomainQualifiedUserWhole()
    {
        var parsed = SmbLoginKey.TryParse("smb-login:server\\DOMAIN\\bob", out var host, out var username);

        Assert.IsTrue(parsed);
        Assert.AreEqual("server", host);
        Assert.AreEqual("DOMAIN\\bob", username);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("mail:alice@example.com")]
    [DataRow("smb-login:")]
    [DataRow("smb-login:host")]
    [DataRow("smb-login:\\bob")]
    [DataRow("smb-login:host\\")]
    public void TryParse_RejectsAnyOtherId(string id)
    {
        Assert.IsFalse(SmbLoginKey.TryParse(id, out _, out _));
    }
}
