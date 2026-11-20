using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using iTerminal.Core.Services.Sharing;

namespace iTerminal.Core.Test.Services.Sharing;

/// <summary>
/// The share over real loopback-free sockets. This is the one test in the unit suite that binds a socket: it is the
/// approved exception, it uses the first free port in 8080 to 8089, and it is a single method so that a bug shows up once.
/// </summary>
[TestClass]
public class FileShareServiceTest
{
    [TestMethod]
    [Timeout(120_000)]
    public async Task ShareLifecycle_ServesOverTlsStopsAndFreesThePort()
    {
        IPAddress? address = FileShareAddress.Choose(new NetworkInterfaceAddressSource().Candidates());
        if (address is null)
        {
            Assert.Inconclusive("This machine has no LAN IPv4 address to serve on.");
        }

        string folder = Path.Combine(Path.GetTempPath(), "iTerminal-service-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "hello.txt");
        File.WriteAllText(file, "hello over tls");
        using FileShareService service = new(new FixedAddressSource(address));
        try
        {
            // Start: the share runs, the URL carries the token, and the state says so.
            FileShareStartResult started = await service.StartAsync([file]);
            Assert.IsTrue(started.Started, $"start failed: {started.FailureKey}");
            Assert.AreEqual(FileShareStatus.Running, service.State.Status, "state after start");
            Assert.IsNotNull(service.State.QrModules, "the code is encoded on the start");
            Uri uri = new(started.Url!);
            string token = uri.AbsolutePath.Trim('/');
            Assert.AreEqual("https", uri.Scheme, "scheme");

            // Served: a valid token gets the file, a wrong token gets nothing.
            string ok = await GetAsync(uri.Host, uri.Port, $"/{token}/hello.txt");
            StringAssert.Contains(ok, "200 OK", "a valid token is served");
            StringAssert.EndsWith(ok, "hello over tls", "the file body is sent");
            string wrong = await GetAsync(uri.Host, uri.Port, "/not-the-token/hello.txt");
            StringAssert.Contains(wrong, "404 Not Found", "a wrong token is refused");

            // Busy: a second start while one runs changes nothing.
            FileShareStartResult second = await service.StartAsync([file]);
            Assert.IsFalse(second.Started, "a second start is refused");
            Assert.AreEqual("FileTransfer.Busy", second.FailureKey, "the refusal says the share is busy");
            Assert.AreEqual(started.Url, service.State.Url, "the running share keeps its URL");

            // Stop: bounded, the state is Stopped, and the port is free again.
            Stopwatch stop = Stopwatch.StartNew();
            await service.StopAsync();
            stop.Stop();
            Assert.IsTrue(stop.Elapsed < TimeSpan.FromSeconds(5), $"stop took {stop.Elapsed}");
            Assert.AreEqual(FileShareStatus.Stopped, service.State.Status, "state after stop");
            await Assert.ThrowsExactlyAsync<SocketException>(() => ConnectAsync(uri.Host, uri.Port), "the port refuses after stop");

            TcpListener probe = new(IPAddress.Any, uri.Port);
            probe.Start();
            probe.Stop();
        }
        finally
        {
            await service.StopAsync();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<string> GetAsync(string host, int port, string target)
    {
        using TcpClient client = await ConnectAsync(host, port);
        using SslStream tls = new(client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        });
        byte[] request = Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n");
        await tls.WriteAsync(request);
        using MemoryStream response = new();
        await tls.CopyToAsync(response);
        return Encoding.UTF8.GetString(response.ToArray());
    }

    private static async Task<TcpClient> ConnectAsync(string host, int port)
    {
        TcpClient client = new();
        await client.ConnectAsync(host, port);
        return client;
    }

    private sealed class FixedAddressSource(IPAddress address) : IFileShareAddressSource
    {
        public IReadOnlyList<LanAddress> Candidates() => [new LanAddress(address, HasDefaultGateway: true)];
    }
}
