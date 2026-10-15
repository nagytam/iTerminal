using System.Net;

namespace iTerminal.Core.Model.Network;

public record DiscoveredHost(
    IPAddress IPAddress,
    bool SshAvailable,
    bool SmbAvailable,
    IReadOnlyList<string> Shares);
