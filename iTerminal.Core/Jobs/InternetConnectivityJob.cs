using iConsole.Jobs;
using iTerminal.Core.Services;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Keeps <see cref="InternetConnectivityService"/> up to date for the menu bar's Online indicator. A check first asks
/// the operating system whether any network interface is up (no traffic); only then does it open a TCP connection to
/// a public resolver. A connection is a handshake and nothing more: no request is sent and no data leaves the machine.
/// </summary>
public class InternetConnectivityJob(InternetConnectivityService connectivityService) : Job
{
    /// <summary>Public endpoints to reach; one that accepts a connection is enough.</summary>
    internal static readonly (string Host, int Port)[] Endpoints = [("1.1.1.1", 443), ("8.8.8.8", 53)];

    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan OnlineInterval = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan OfflineInterval = TimeSpan.FromSeconds(15);

    private readonly InternetConnectivityService _connectivityService = connectivityService;

    /// <summary>How long after start the first check goes out.</summary>
    public TimeSpan FirstCheckDelay { get; init; } = StartupDelays.FirstNetworkRefresh;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var online = false;
            try
            {
                online = await IsInternetReachableAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"{nameof(InternetConnectivityJob)}.{nameof(RunAsync)} failed: {exception}");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _connectivityService.Report(online);

            try
            {
                await Task.Delay(online ? OnlineInterval : OfflineInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>True when an interface is up and one of <see cref="Endpoints"/> accepts a TCP connection.</summary>
    internal static async Task<bool> IsInternetReachableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return false;
        }

        foreach (var (host, port) in Endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                await socket.ConnectAsync(host, port, timeout.Token);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // This endpoint timed out; try the next one.
            }
            catch (SocketException)
            {
                // Refused or unreachable; try the next one.
            }
        }

        return false;
    }
}
