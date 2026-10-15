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
        if (smbAvailable)
        {
            try
            {
                shares = [.. smbFileSystemService.ListShares(address.ToString(), string.Empty, string.Empty)];
            }
            catch
            {
                // Best-effort: many hosts reject anonymous share enumeration — that's fine,
                // the user can still connect and supply credentials from the browser dialog.
            }
        }

        return new DiscoveredHost(address, sshAvailable, smbAvailable, shares);
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
                addresses.AddRange(EnumerateLocalSubnet(unicast.Address));
            }
        }
        return [.. addresses.Distinct()];
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
