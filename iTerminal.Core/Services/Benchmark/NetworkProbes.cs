using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace iTerminal.Core.Services.Benchmark;

/// <summary>
/// The public speed-test server the internet probes ask. Its answer is a number of bytes the request names, so a probe
/// downloads exactly what it measures, and <c>bytes=0</c> measures the round trip alone.
/// </summary>
internal static class InternetProbeServer
{
    internal static readonly Uri DefaultBase = new("https://speed.cloudflare.com/");

    internal static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    internal static Uri Download(Uri baseAddress, long bytes) => new(baseAddress, $"__down?bytes={bytes}");
}

/// <summary>
/// Moves bytes over a TCP connection on this machine, to a listener on the loopback address. It measures the network stack
/// and the socket layer, and never leaves the machine, so it works offline.
/// </summary>
public sealed class NetworkLoopbackProbe : IBenchmarkProbe
{
    internal const long DefaultBytes = 128L * 1024 * 1024;

    private readonly long _bytes;

    public NetworkLoopbackProbe() : this(DefaultBytes)
    {
    }

    internal NetworkLoopbackProbe(long bytes) => _bytes = bytes;

    public string Id => "network.loopback";

    public string Name => "Loopback throughput";

    public BenchmarkCategory Category => BenchmarkCategory.Network;

    public async Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using CancellationTokenRegistration registration = cancellationToken.Register(listener.Stop);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            long bytes = _bytes;
            Task sending = Task.Run(async () =>
            {
                using TcpClient server = await listener.AcceptTcpClientAsync(cancellationToken);
                using NetworkStream serverStream = server.GetStream();
                byte[] chunk = new byte[64 * 1024];
                long sent = 0;
                while (sent < bytes)
                {
                    int count = (int)Math.Min(chunk.Length, bytes - sent);
                    await serverStream.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
                    sent += count;
                }
            }, cancellationToken);

            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            using NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[64 * 1024];
            long received = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (received < bytes)
            {
                int count = await stream.ReadAsync(buffer, cancellationToken);
                if (count == 0)
                {
                    break;
                }

                received += count;
            }

            stopwatch.Stop();
            await sending;
            return new BenchmarkMeasurement(received / 1e6 / stopwatch.Elapsed.TotalSeconds, "MB/s", 1000, HigherIsBetter: true);
        }
        finally
        {
            listener.Stop();
        }
    }
}

/// <summary>The median of five round trips to the speed-test server, in milliseconds. The median keeps the first, connection-setting-up request from deciding it.</summary>
public sealed class NetworkLatencyProbe : IBenchmarkProbe
{
    private readonly Uri _baseAddress;

    public NetworkLatencyProbe() : this(InternetProbeServer.DefaultBase)
    {
    }

    internal NetworkLatencyProbe(Uri baseAddress) => _baseAddress = baseAddress;

    public string Id => "network.latency";

    public string Name => "Internet latency";

    public BenchmarkCategory Category => BenchmarkCategory.Network;

    public async Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken)
    {
        Uri address = InternetProbeServer.Download(_baseAddress, 0);
        var samples = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            try
            {
                long start = Stopwatch.GetTimestamp();
                using HttpResponseMessage response = await InternetProbeServer.Client.GetAsync(address, cancellationToken);
                response.EnsureSuccessStatusCode();
                await response.Content.ReadAsByteArrayAsync(cancellationToken);
                samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            catch (Exception exception) when ((exception is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                return BenchmarkMeasurement.Unavailable("ms", 30, false, $"could not reach {_baseAddress.Host}");
            }
        }

        samples.Sort();
        return new BenchmarkMeasurement(samples[samples.Count / 2], "ms", 30, HigherIsBetter: false);
    }
}

/// <summary>Downloads 10 MB from the speed-test server and reports the megabytes a second it arrived at.</summary>
public sealed class NetworkDownloadProbe : IBenchmarkProbe
{
    internal const long DefaultBytes = 10L * 1000 * 1000;

    private readonly Uri _baseAddress;
    private readonly long _bytes;

    public NetworkDownloadProbe() : this(InternetProbeServer.DefaultBase, DefaultBytes)
    {
    }

    internal NetworkDownloadProbe(Uri baseAddress, long bytes)
    {
        _baseAddress = baseAddress;
        _bytes = bytes;
    }

    public string Id => "network.download";

    public string Name => "Internet download";

    public BenchmarkCategory Category => BenchmarkCategory.Network;

    public async Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await InternetProbeServer.Client.GetAsync(
                InternetProbeServer.Download(_baseAddress, _bytes), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            byte[] buffer = new byte[64 * 1024];
            long received = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                received += count;
            }

            stopwatch.Stop();
            return new BenchmarkMeasurement(received / 1e6 / stopwatch.Elapsed.TotalSeconds, "MB/s", 12.5, HigherIsBetter: true);
        }
        catch (Exception exception) when ((exception is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return BenchmarkMeasurement.Unavailable("MB/s", 12.5, true, $"could not reach {_baseAddress.Host}");
        }
    }
}
