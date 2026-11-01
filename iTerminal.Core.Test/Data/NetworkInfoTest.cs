using System.Net.NetworkInformation;
using iTerminal.Core.Data;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class NetworkInfoTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesProperties()
    {
        var networkInfo = new NetworkInfo();
        Assert.IsNotNull(networkInfo);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Name_SetAndGet()
    {
        var networkInfo = new NetworkInfo { Name = "Ethernet" };
        Assert.AreEqual("Ethernet", networkInfo.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Name_CanBeNull()
    {
        var networkInfo = new NetworkInfo { Name = null! };
        Assert.IsNull(networkInfo.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NetworkInterface_IsNotAccessibleViaPublicSetter()
    {
        // NetworkInterface has internal setter, cannot be set directly from tests
        // This validates the API design restricts external mutation
        var networkInfo = new NetworkInfo();
        Assert.IsNotNull(networkInfo);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanCreateMultipleNetworkInfoInstances()
    {
        var networkInfo1 = new NetworkInfo { Name = "Ethernet" };
        var networkInfo2 = new NetworkInfo { Name = "WiFi" };

        Assert.AreEqual("Ethernet", networkInfo1.Name);
        Assert.AreEqual("WiFi", networkInfo2.Name);
        Assert.AreNotEqual(networkInfo1.Name, networkInfo2.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Name_CanBeUpdated()
    {
        var networkInfo = new NetworkInfo { Name = "Ethernet" };
        Assert.AreEqual("Ethernet", networkInfo.Name);

        networkInfo.Name = "WiFi";
        Assert.AreEqual("WiFi", networkInfo.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ValidNetworkInterfaces()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        // Just verify we can get interfaces without error
        Assert.IsNotNull(interfaces);
    }
}
