using iTerminal.Core.Model.Network;

namespace iTerminal.Core.Services;

public interface INetworkDiscoveryService
{
    /// <summary>Streams hosts on the local /24 subnet(s) that respond on the SSH (22) and/or
    /// SMB (445) port, as they're found. Best-effort: unreachable/filtered hosts are simply
    /// absent from the results, nothing throws for them.</summary>
    IAsyncEnumerable<DiscoveredHost> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>The shares <paramref name="host"/> lists for <paramref name="username"/>; null when the login is refused or
    /// the host cannot be reached. The SMB call is blocking, so it runs off the caller's thread.</summary>
    Task<IReadOnlyList<string>?> ListSharesWithLoginAsync(string host, string username, string password, CancellationToken cancellationToken = default);
}
