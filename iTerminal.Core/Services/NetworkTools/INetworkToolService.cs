namespace iTerminal.Core.Services.NetworkTools;

/// <summary>The network utilities the Network tools dialog offers.</summary>
public enum NetworkToolKind
{
    Ping,
    Traceroute,
    DnsLookup,
    ReverseDns,
    Whois,
    PortCheck,
    HttpHeaders,
    Scan,
}

/// <summary>
/// Runs one network utility against a host, address or web address the user typed. Output arrives a line at a time through
/// <c>writeLine</c>, so a slow tool (a ping that times out, a whois referral) shows what it has as it goes.
/// </summary>
public interface INetworkToolService
{
    /// <summary>
    /// Runs <paramref name="kind"/> against <paramref name="target"/>. A target the tool cannot use throws
    /// <see cref="ArgumentException"/> before anything is sent; a failure while running is written as a line instead.
    /// </summary>
    Task RunAsync(NetworkToolKind kind, string target, Action<string> writeLine, CancellationToken cancellationToken = default);
}
