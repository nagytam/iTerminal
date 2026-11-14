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
