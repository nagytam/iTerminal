using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;
using Moq;
using System.Net;
using System.Net.Sockets;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class NetworkDiscoveryServiceTest
{
    private static (TcpListener listener, int port) StartLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, port);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ProbePortAsync_OpenPort_ReturnsTrue()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            var result = await NetworkDiscoveryService.ProbePortAsync(IPAddress.Loopback, port, CancellationToken.None);
            Assert.IsTrue(result);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ProbePortAsync_ClosedPort_ReturnsFalse()
    {
        var (listener, port) = StartLoopbackListener();
        listener.Stop();

        var result = await NetworkDiscoveryService.ProbePortAsync(IPAddress.Loopback, port, CancellationToken.None);

        Assert.IsFalse(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ProbePortAsync_UnreachableAddress_ReturnsFalseRatherThanThrowing()
    {
        // TEST-NET-1 (RFC 5737) — reserved for documentation, guaranteed unreachable/non-routed.
        var result = await NetworkDiscoveryService.ProbePortAsync(IPAddress.Parse("192.0.2.1"), 22, CancellationToken.None);

        Assert.IsFalse(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ScanAsync_HostWithOpenSshPort_IsReturnedAsSshAvailable()
    {
        var (sshListener, sshPort) = StartLoopbackListener();
        var (smbListener, smbPort) = StartLoopbackListener();
        smbListener.Stop(); // SMB port stays closed — only SSH should be reported.
        try
        {
            var service = new NetworkDiscoveryService(Mock.Of<ISmbFileSystemService>())
            {
                SshPort = sshPort,
                SmbPort = smbPort,
            };
            var addresses = new List<IPAddress> { IPAddress.Loopback };

            var results = new List<DiscoveredHost>();
            await foreach (var host in service.ScanAsync(addresses, CancellationToken.None))
            {
                results.Add(host);
            }

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].SshAvailable);
            Assert.IsFalse(results[0].SmbAvailable);
        }
        finally
        {
            sshListener.Stop();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ScanAsync_HostWithOpenSmbPort_ListsSharesBestEffort()
    {
        var (sshListener, sshPort) = StartLoopbackListener();
        sshListener.Stop();
        var (smbListener, smbPort) = StartLoopbackListener();
        try
        {
            var smbFileSystemService = new Mock<ISmbFileSystemService>();
            smbFileSystemService.Setup(s => s.ListShares(IPAddress.Loopback.ToString(), string.Empty, string.Empty))
                .Returns(["Public"]);
            var service = new NetworkDiscoveryService(smbFileSystemService.Object)
            {
                SshPort = sshPort,
                SmbPort = smbPort,
            };
            var addresses = new List<IPAddress> { IPAddress.Loopback };

            var results = new List<DiscoveredHost>();
            await foreach (var host in service.ScanAsync(addresses, CancellationToken.None))
            {
                results.Add(host);
            }

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].SmbAvailable);
            CollectionAssert.Contains(results[0].Shares.ToList(), "Public");
        }
        finally
        {
            smbListener.Stop();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ScanAsync_NoAddresses_CompletesImmediatelyWithNoResults()
    {
        var service = new NetworkDiscoveryService(Mock.Of<ISmbFileSystemService>());

        var results = new List<DiscoveredHost>();
        await foreach (var host in service.ScanAsync([], CancellationToken.None))
        {
            results.Add(host);
        }

        Assert.AreEqual(0, results.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ScanAsync_UnreachableAddresses_ReturnsNoHosts()
    {
        var service = new NetworkDiscoveryService(Mock.Of<ISmbFileSystemService>());
        var addresses = new List<IPAddress>
        {
            IPAddress.Parse("192.0.2.1"),
            IPAddress.Parse("192.0.2.2"),
        };

        var results = new List<DiscoveredHost>();
        await foreach (var host in service.ScanAsync(addresses, CancellationToken.None))
        {
            results.Add(host);
        }

        Assert.AreEqual(0, results.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EnumerateLocalSubnet_ReturnsRangeExcludingSelf()
    {
        var address = IPAddress.Parse("192.168.1.42");

        var results = NetworkDiscoveryService.EnumerateLocalSubnet(address).ToList();

        Assert.AreEqual(253, results.Count);
        Assert.IsFalse(results.Any(a => a.Equals(address)));
        Assert.IsTrue(results.Any(a => a.Equals(IPAddress.Parse("192.168.1.1"))));
        Assert.IsTrue(results.Any(a => a.Equals(IPAddress.Parse("192.168.1.254"))));
        Assert.IsFalse(results.Any(a => a.Equals(IPAddress.Parse("192.168.1.0"))));
        Assert.IsFalse(results.Any(a => a.Equals(IPAddress.Parse("192.168.1.255"))));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetCandidateAddresses_DoesNotThrow()
    {
        var addresses = NetworkDiscoveryService.GetCandidateAddresses();

        Assert.IsNotNull(addresses);
    }
}
