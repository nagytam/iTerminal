using System.Net;
using iTerminal.Core.Services.Sharing;

namespace iTerminal.Core.Test.Services.Sharing;

[TestClass]
public class FileShareAddressTest
{
    [TestMethod]
    public void Choose_PrefersThePrivateAddressWithADefaultGateway()
    {
        IPAddress? chosen = FileShareAddress.Choose(
        [
            new LanAddress(IPAddress.Parse("10.0.0.5"), HasDefaultGateway: false),
            new LanAddress(IPAddress.Parse("192.168.1.20"), HasDefaultGateway: true),
        ]);

        Assert.AreEqual(IPAddress.Parse("192.168.1.20"), chosen);
    }

    [TestMethod]
    public void Choose_NeverPicksLoopbackOrLinkLocal()
    {
        IPAddress? chosen = FileShareAddress.Choose(
        [
            new LanAddress(IPAddress.Loopback, HasDefaultGateway: true),
            new LanAddress(IPAddress.Parse("169.254.10.10"), HasDefaultGateway: true),
            new LanAddress(IPAddress.Parse("8.8.8.8"), HasDefaultGateway: true),
        ]);

        Assert.IsNull(chosen, "a public address is not a LAN address");
    }

    [TestMethod]
    public void Choose_FallsBackToAPrivateAddressWithoutAGateway()
    {
        IPAddress? chosen = FileShareAddress.Choose(
        [
            new LanAddress(IPAddress.Parse("172.20.0.4"), HasDefaultGateway: false),
        ]);

        Assert.AreEqual(IPAddress.Parse("172.20.0.4"), chosen);
    }

    [TestMethod]
    public void Choose_WithNoCandidates_IsNull()
    {
        Assert.IsNull(FileShareAddress.Choose([]));
    }
}
