using iTerminal.Core.Model.Network;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace iTerminal.Core.Services;

public class NetworkDiscoveryService(ISmbFileSystemService smbFileSystemService) : INetworkDiscoveryService
{
    private const int MaxConcurrency = 64;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);

    // Overridable only from tests (InternalsVisibleTo), so probing can be pointed at a local
    // TcpListener instead of the real SSH/SMB ports.
    internal int SshPort { get; init; } = 22;
    internal int SmbPort { get; init; } = 445;

    public IAsyncEnumerable<DiscoveredHost> ScanAsync(CancellationToken cancellationToken = default)
    {
        return ScanAsync(GetCandidateAddresses(), cancellationToken);
    }

    public Task<SmbListing?> ListSharesWithLoginAsync(string host, string username, string password, CancellationToken cancellationToken = default) =>
        Task.Run(() => smbFileSystemService.TryListSharesWithVersion(host, username, password), cancellationToken);

    internal async IAsyncEnumerable<DiscoveredHost> ScanAsync(
        IEnumerable<IPAddress> addresses,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<DiscoveredHost>();
        using var semaphore = new SemaphoreSlim(MaxConcurrency);

        var scanTask = Task.Run(async () =>
        {
            var probeTasks = addresses.Select(async address =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var host = await ProbeAsync(address, cancellationToken);
                    if (host is not null)
                    {
                        await channel.Writer.WriteAsync(host, cancellationToken);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });
            await Task.WhenAll(probeTasks);
            channel.Writer.Complete();
        }, cancellationToken);

        await foreach (var host in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return host;
        }

        await scanTask;
    }

    private async Task<DiscoveredHost?> ProbeAsync(IPAddress address, CancellationToken cancellationToken)
    {
        var sshTask = ProbePortAsync(address, SshPort, cancellationToken);
        var smbTask = ProbePortAsync(address, SmbPort, cancellationToken);
        await Task.WhenAll(sshTask, smbTask);
        var sshAvailable = sshTask.Result;
        var smbAvailable = smbTask.Result;

        if (!sshAvailable && !smbAvailable)
        {
            return null;
        }

        IReadOnlyList<string> shares = [];
        var smbVersion = 0;
        if (smbAvailable)
        {
            try
            {
                // Null when the anonymous login is refused: many hosts reject it, and the user can still log in from the page.
                if (smbFileSystemService.TryListSharesWithVersion(address.ToString(), string.Empty, string.Empty) is { } listing)
                {
                    shares = listing.Shares;
                    smbVersion = listing.SmbVersion;
                }
            }
            catch
            {
                // Best-effort: the host is still listed, without shares.
            }
        }

        return new DiscoveredHost(address, sshAvailable, smbAvailable, shares, smbVersion);
    }

    internal static async Task<bool> ProbePortAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ProbeTimeout);
            await client.ConnectAsync(address, port, timeoutCts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    internal static List<IPAddress> GetCandidateAddresses()
    {
        var addresses = new List<IPAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }
                // This machine's own address first, so its shares are listed even when no other host answers.
                addresses.Add(unicast.Address);
                addresses.AddRange(EnumerateLocalSubnet(unicast.Address));
            }
        }
        if (addresses.Count == 0)
        {
            // No active network: the machine is still reachable on loopback.
            addresses.Add(IPAddress.Loopback);
        }
        return [.. addresses.Distinct()];
    }

    /// <summary>True when <paramref name="address"/> belongs to this machine.</summary>
    internal static bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Any(unicast => unicast.Address.Equals(address));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Enumerates the /24 range anchored at <paramref name="address"/>'s own subnet
    /// (its first three octets), regardless of the interface's actual (possibly much larger)
    /// subnet mask — keeps scan time bounded at the cost of not covering wider subnets.</summary>
    internal static IEnumerable<IPAddress> EnumerateLocalSubnet(IPAddress address)
    {
        var baseBytes = address.GetAddressBytes();
        for (var host = 1; host <= 254; host++)
        {
            var candidate = (byte[])baseBytes.Clone();
            candidate[3] = (byte)host;
            var candidateAddress = new IPAddress(candidate);
            if (!candidateAddress.Equals(address))
            {
                yield return candidateAddress;
            }
        }
    }
}
