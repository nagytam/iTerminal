using iTerminal.Core.Model.Network;

namespace iTerminal.Core.Services;

public interface INetworkDiscoveryService
{
    /// <summary>Streams hosts on the local /24 subnet(s) that respond on the SSH (22) and/or
    /// SMB (445) port, as they're found. Best-effort: unreachable/filtered hosts are simply
    /// absent from the results, nothing throws for them.</summary>
    IAsyncEnumerable<DiscoveredHost> ScanAsync(CancellationToken cancellationToken = default);
}
