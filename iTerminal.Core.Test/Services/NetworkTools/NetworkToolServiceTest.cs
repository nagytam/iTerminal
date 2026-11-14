using System.Net;
using System.Net.Sockets;
using iTerminal.Core.Services.NetworkTools;

namespace iTerminal.Core.Test.Services.NetworkTools;

[TestClass]
public class NetworkToolServiceTest
{
    private static async Task<List<string>> RunAsync(NetworkToolKind kind, string target)
    {
        var lines = new List<string>();
        await new NetworkToolService().RunAsync(kind, target, lines.Add);
        return lines;
    }

    /// <summary>A loopback port that is open while the listener is up; closed again once it is stopped.</summary>
    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APortThatAcceptsConnectionsIsReportedOpen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            List<string> lines = await RunAsync(NetworkToolKind.PortCheck, $"127.0.0.1:{port}");
            Assert.IsTrue(lines.Any(line => line.Contains($"Port {port} on 127.0.0.1 is open", StringComparison.Ordinal)), string.Join("\n", lines));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APortNothingListensOnIsReportedClosed()
    {
        int port = FreeLoopbackPort();
        List<string> lines = await RunAsync(NetworkToolKind.PortCheck, $"127.0.0.1:{port}");
        Assert.IsTrue(lines.Any(line => line.Contains("is closed or unreachable", StringComparison.Ordinal)), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AReverseLookupOfAnUnusableAddressIsRefusedBeforeAnythingIsSent()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => RunAsync(NetworkToolKind.ReverseDns, "not an address"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APortCheckWithoutAPortIsRefusedBeforeAnythingIsSent()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => RunAsync(NetworkToolKind.PortCheck, "example.com"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APingTargetWithAPathIsRefusedBeforeAnythingIsSent()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => RunAsync(NetworkToolKind.Ping, "example.com/index"));
    }
}
