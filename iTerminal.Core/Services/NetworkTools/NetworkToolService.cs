using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;

namespace iTerminal.Core.Services.NetworkTools;

/// <summary>
/// The network utilities, written in .NET so they work the same on every operating system: ICMP ping and traceroute, DNS
/// lookups, whois over TCP port 43 (following the registry's referrals), a TCP port check and the headers of a web address.
/// Each one contacts only the target the user typed (whois also contacts the whois servers it is referred to).
/// </summary>
public sealed class NetworkToolService : INetworkToolService
{
    internal const int PingCount = 4;
    internal const int PingTimeoutMilliseconds = 2000;
    internal const int MaxHops = 30;
    internal const int WhoisPort = 43;
    internal const int WhoisMaxReferrals = 2;
    internal const int WhoisMaxLines = 300;
    internal const string WhoisRegistry = "whois.iana.org";

    private static readonly TimeSpan PortTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WhoisTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    internal const int ScanMaxProbes = 32;
    internal const int ScanMaxLookups = 8;
    internal const int ScanProgressEvery = 64;
    internal const int ReverseLookupTimeoutMilliseconds = 2000;

    private readonly Func<IPAddress, CancellationToken, Task<TimeSpan?>> _probe;
    private readonly Func<IPAddress, CancellationToken, Task<string?>> _reverseLookup;
    private readonly TimeSpan _reverseLookupTimeout;

    // Shared by every scan this service runs. A lookup holds its slot until the DNS call returns, even after it was given up,
    // because the call cannot be cancelled; so at most ScanMaxLookups lookup calls exist at once.
    private readonly SemaphoreSlim _lookupGate = new(ScanMaxLookups, ScanMaxLookups);

    public NetworkToolService()
        : this(ProbeAsync, ReverseLookupAsync, TimeSpan.FromMilliseconds(ReverseLookupTimeoutMilliseconds))
    {
    }

    /// <summary>The service with its probe and reverse lookup supplied, so a test can stand in for ICMP and DNS.</summary>
    internal NetworkToolService(
        Func<IPAddress, CancellationToken, Task<TimeSpan?>> probe,
        Func<IPAddress, CancellationToken, Task<string?>> reverseLookup,
        TimeSpan reverseLookupTimeout)
    {
        _probe = probe;
        _reverseLookup = reverseLookup;
        _reverseLookupTimeout = reverseLookupTimeout;
    }

    public async Task RunAsync(NetworkToolKind kind, string target, Action<string> writeLine, CancellationToken cancellationToken = default)
    {
        // The target is checked before anything is sent, so a bad one throws ArgumentException to the caller.
        Func<CancellationToken, Task> run = kind switch
        {
            NetworkToolKind.Ping => ct => PingAsync(NetworkToolTargets.ParseHost(target), writeLine, ct),
            NetworkToolKind.Traceroute => ct => TracerouteAsync(NetworkToolTargets.ParseHost(target), writeLine, ct),
            NetworkToolKind.DnsLookup => ct => DnsLookupAsync(NetworkToolTargets.ParseHost(target), writeLine, ct),
            NetworkToolKind.ReverseDns => ct => ReverseDnsAsync(NetworkToolTargets.ParseIpAddress(target), writeLine, ct),
            NetworkToolKind.Whois => ct => WhoisAsync(NetworkToolTargets.ParseHost(target), writeLine, ct),
            NetworkToolKind.PortCheck => ct =>
            {
                (string host, int port) = NetworkToolTargets.ParseHostAndPort(target);
                return PortCheckAsync(host, port, writeLine, ct);
            },
            NetworkToolKind.HttpHeaders => ct => HttpHeadersAsync(NetworkToolTargets.ParseWebAddress(target), writeLine, ct),
            NetworkToolKind.Scan => ct => ScanAsync(NetworkToolTargets.ParseSubnet(target), writeLine, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        try
        {
            await run(cancellationToken);
        }
        catch (Exception exception) when (exception is not (ArgumentException or OperationCanceledException))
        {
            // A failure while running is an answer too: the reason goes on the screen instead of ending the run silently.
            writeLine(exception.Message);
        }
    }

    private static async Task PingAsync(string host, Action<string> writeLine, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        int received = 0;
        writeLine($"Pinging {host} with {PingCount} echo requests:");
        for (int i = 0; i < PingCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PingReply reply = await ping.SendPingAsync(host, PingTimeoutMilliseconds);
            if (reply.Status == IPStatus.Success)
            {
                received++;
                writeLine($"Reply from {reply.Address}: time={reply.RoundtripTime} ms TTL={reply.Options?.Ttl ?? 0}");
            }
            else
            {
                writeLine(DescribePingStatus(reply.Status));
            }

            if (i < PingCount - 1)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        int lost = PingCount - received;
        writeLine($"Sent {PingCount}, received {received}, lost {lost} ({lost * 100 / PingCount}%).");
    }

    private static async Task TracerouteAsync(string host, Action<string> writeLine, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        byte[] buffer = new byte[32];
        writeLine($"Tracing the route to {host} over at most {MaxHops} hops:");
        for (int ttl = 1; ttl <= MaxHops; ttl++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PingReply reply = await ping.SendPingAsync(host, PingTimeoutMilliseconds, buffer, new PingOptions(ttl, true));
            bool answered = reply.Status is IPStatus.Success or IPStatus.TtlExpired;
            string hop = answered ? reply.Address.ToString() : "*";
            string time = answered ? $"{reply.RoundtripTime} ms" : string.Empty;
            string note = answered || reply.Status == IPStatus.TimedOut ? string.Empty : DescribePingStatus(reply.Status);
            writeLine($"{ttl,3}  {hop,-40} {time,-8} {note}".TrimEnd());

            if (reply.Status == IPStatus.Success)
            {
                writeLine("Trace complete.");
                return;
            }
        }

        writeLine($"Stopped after {MaxHops} hops without reaching {host}.");
    }

    private static async Task DnsLookupAsync(string host, Action<string> writeLine, CancellationToken cancellationToken)
    {
        IPHostEntry entry = await Dns.GetHostEntryAsync(host, cancellationToken);
        writeLine($"Name:    {entry.HostName}");
        foreach (string alias in entry.Aliases)
        {
            writeLine($"Alias:   {alias}");
        }

        foreach (IPAddress address in entry.AddressList)
        {
            writeLine($"Address: {address} ({FamilyName(address)})");
        }

        if (entry.AddressList.Length == 0)
        {
            writeLine("The name resolves to no address.");
        }
    }

    private static async Task ReverseDnsAsync(IPAddress address, Action<string> writeLine, CancellationToken cancellationToken)
    {
        // Given an address as text, GetHostEntryAsync looks it up in reverse.
        IPHostEntry entry = await Dns.GetHostEntryAsync(address.ToString(), cancellationToken);
        writeLine($"Address: {address} ({FamilyName(address)})");
        writeLine(entry.HostName == address.ToString()
            ? "No host name is registered for this address."
            : $"Name:    {entry.HostName}");
    }

    private static async Task WhoisAsync(string query, Action<string> writeLine, CancellationToken cancellationToken)
    {
        string server = WhoisRegistry;
        for (int referral = 0; ; referral++)
        {
            writeLine($"Asking {server} about {query}…");
            string response = await QueryWhoisAsync(server, query, cancellationToken);
            string? next = NetworkToolTargets.ParseWhoisReferral(response);
            if (next is null || referral == WhoisMaxReferrals || string.Equals(next, server, StringComparison.OrdinalIgnoreCase))
            {
                WriteWhoisAnswer(response, writeLine);
                return;
            }

            server = next;
        }
    }

    private static async Task<string> QueryWhoisAsync(string server, string query, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WhoisTimeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(server, WhoisPort, timeout.Token);
            await using NetworkStream stream = client.GetStream();
            byte[] request = Encoding.ASCII.GetBytes(query + "\r\n");
            await stream.WriteAsync(request, timeout.Token);
            using var answer = new MemoryStream();
            await stream.CopyToAsync(answer, timeout.Token);
            return Encoding.UTF8.GetString(answer.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException($"{server} did not answer within {WhoisTimeout.TotalSeconds} seconds.");
        }
    }

    private static void WriteWhoisAnswer(string response, Action<string> writeLine)
    {
        int written = 0;
        foreach (string raw in response.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (written == WhoisMaxLines)
            {
                writeLine($"… the answer is longer; the first {WhoisMaxLines} lines are shown.");
                return;
            }

            writeLine(line);
            written++;
        }

        if (written == 0)
        {
            writeLine("The whois server sent no answer.");
        }
    }

    private static async Task PortCheckAsync(string host, int port, Action<string> writeLine, CancellationToken cancellationToken)
    {
        writeLine($"Connecting to {host} on port {port}…");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PortTimeout);
        var watch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            writeLine($"Port {port} on {host} is open ({watch.ElapsedMilliseconds} ms).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            writeLine($"No answer within {PortTimeout.TotalSeconds} seconds: the port is filtered, or the host is down.");
        }
        catch (SocketException exception)
        {
            writeLine($"Port {port} on {host} is closed or unreachable: {exception.Message}");
        }
    }

    private static async Task HttpHeadersAsync(Uri uri, Action<string> writeLine, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = HttpTimeout };
        using var request = new HttpRequestMessage(HttpMethod.Head, uri);
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        writeLine($"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}");
        writeLine($"Address: {response.RequestMessage?.RequestUri ?? uri}");
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
        {
            writeLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }
    }

    /// <summary>
    /// Pings every usable address of the subnet, at most <see cref="ScanMaxProbes"/> at a time. A live host's line waits for its
    /// reverse lookup (at most <c>_reverseLookupTimeout</c>), so each line is written once, with its name if one arrived in time.
    /// The summary follows the last line. A cancelled scan returns at once and writes nothing after it returns.
    /// </summary>
    private async Task ScanAsync(IReadOnlyList<IPAddress> addresses, Action<string> writeLine, CancellationToken cancellationToken)
    {
        var output = new ScanOutput(writeLine);
        var counts = new ScanCounts();
        var probeGate = new SemaphoreSlim(ScanMaxProbes, ScanMaxProbes);
        var hosts = new List<Task>(addresses.Count);
        var stopped = new TaskCompletionSource();
        // Closing the output on cancel, not only on return, means no line can follow the cancel request. The registration is
        // released when the scan returns, so a long-lived token does not keep one per scan.
        using CancellationTokenRegistration closeOnCancel = cancellationToken.Register(() =>
        {
            output.Close();
            stopped.TrySetResult();
        });
        try
        {
            foreach (IPAddress address in addresses)
            {
                await probeGate.WaitAsync(cancellationToken);
                hosts.Add(ScanHostAsync(address, probeGate, output, counts, addresses.Count, cancellationToken));
            }

            // Waiting for the held lines stops at Esc: a probe still in flight is left to finish on its own.
            Task all = Task.WhenAll(hosts);
            await Task.WhenAny(all, stopped.Task);
            cancellationToken.ThrowIfCancellationRequested();
            await all;
            output.Write($"Scanned {addresses.Count} addresses, {Volatile.Read(ref counts.Answered)} hosts answered. Hosts that block ping do not appear.");
        }
        finally
        {
            output.Close();
        }
    }

    private async Task ScanHostAsync(IPAddress address, SemaphoreSlim probeGate, ScanOutput output, ScanCounts counts, int total, CancellationToken cancellationToken)
    {
        try
        {
            TimeSpan? roundTrip = await ProbeOnceAsync(address, cancellationToken);
            probeGate.Release();
            cancellationToken.ThrowIfCancellationRequested();

            int completed = Interlocked.Increment(ref counts.Completed);
            if (completed % ScanProgressEvery == 0 && completed < total)
            {
                output.Write($"Scanned {completed} of {total} addresses.");
            }

            if (roundTrip is not { } time)
            {
                return;
            }

            string? name = await LookupNameAsync(address, cancellationToken);
            Interlocked.Increment(ref counts.Answered);
            output.Write(FormatHost(address, time, name));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The scan was cancelled: this host's line is dropped, and nothing is written after the scan returned.
        }
    }

    /// <summary>A probe that fails (a refusal to send ICMP, or anything else) is no reply for that host.</summary>
    private async Task<TimeSpan?> ProbeOnceAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            return await _probe(address, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The name for a live host, or null. No free lookup slot means no name, at once; a lookup that is not done within the timeout
    /// means no name, and its slot stays taken until the DNS call really returns.
    /// </summary>
    private async Task<string?> LookupNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        if (!_lookupGate.Wait(0))
        {
            return null;
        }

        Task<string?> lookup = LookupOnceAsync(address, cancellationToken);
        try
        {
            return await lookup.WaitAsync(_reverseLookupTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private async Task<string?> LookupOnceAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            return await _reverseLookup(address, cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _lookupGate.Release();
        }
    }

    private static string FormatHost(IPAddress address, TimeSpan roundTrip, string? name)
    {
        string line = $"{address.ToString().PadRight(15)} time={(long)roundTrip.TotalMilliseconds} ms";
        return name is null ? line : $"{line}  {name}";
    }

    private static async Task<TimeSpan?> ProbeAsync(IPAddress address, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        PingReply reply = await ping.SendPingAsync(address, PingTimeoutMilliseconds);
        return reply.Status == IPStatus.Success ? TimeSpan.FromMilliseconds(reply.RoundtripTime) : null;
    }

    private static async Task<string?> ReverseLookupAsync(IPAddress address, CancellationToken cancellationToken)
    {
        // Given an address as text, GetHostEntryAsync looks it up in reverse.
        IPHostEntry entry = await Dns.GetHostEntryAsync(address.ToString(), cancellationToken);
        return entry.HostName == address.ToString() ? null : entry.HostName;
    }

    /// <summary>Writes lines to the output until the scan has returned; after <see cref="Close"/> a late line is dropped.</summary>
    private sealed class ScanOutput(Action<string> writeLine)
    {
        private readonly object _lock = new();
        private bool _closed;

        public void Write(string line)
        {
            lock (_lock)
            {
                if (!_closed)
                {
                    writeLine(line);
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                _closed = true;
            }
        }
    }

    private sealed class ScanCounts
    {
        public int Completed;
        public int Answered;
    }

    internal static string DescribePingStatus(IPStatus status) => status switch
    {
        IPStatus.TimedOut => "Request timed out.",
        IPStatus.DestinationHostUnreachable => "Destination host unreachable.",
        IPStatus.DestinationNetworkUnreachable => "Destination network unreachable.",
        IPStatus.DestinationUnreachable => "Destination unreachable.",
        IPStatus.TtlExpired => "TTL expired in transit.",
        _ => status.ToString(),
    };

    private static string FamilyName(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
}
