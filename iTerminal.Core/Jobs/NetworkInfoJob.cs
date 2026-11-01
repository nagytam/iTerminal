using iTerminal.Core.Services.BackgroundStart;
using iConsole.Jobs;
using iTerminal.Core.Data;
using iTerminal.Core.Model;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("iTerminal.Core.Test")]

namespace iTerminal.Core.Jobs;

public class NetworkInfoJob(NetworkInfoModel networkInfoModel, IBackgroundStartService backgroundStart) : Job
{
    private readonly record struct NetworkInfoSnapshot(
        string? Name,
        string? Description,
        OperationalStatus? OperationalStatus,
        string? Id,
        NetworkInterfaceType? NetworkInterfaceType);

    private NetworkInfoModel _networkInfoModel { get; set; } = networkInfoModel;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        // The interfaces are read when the network manager is opened, or as configured (BackgroundStart.NetworkInfo).
        await backgroundStart.WaitForStartAsync(BackgroundServices.NetworkInfo, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces().ToList();
                var networkInfos = networkInterfaces
                    .Select(networkInterface => new NetworkInfo
                    {
                        NetworkInterface = networkInterface,
                        Name = networkInterface.Name.PadRight(16)
                    })
                    .ToList();

                var existingNetworks = _networkInfoModel.Networks.AsSnapshot();

                foreach (var networkInfo in networkInfos)
                {
                    Update(networkInfo, cancellationToken);
                }

                foreach (var staleNetwork in existingNetworks.Where(existing => networkInfos.All(current => current.Name != existing.Name)).ToList())
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var staleIndex = _networkInfoModel.Networks.IndexOf(staleNetwork);
                    if (staleIndex >= 0)
                    {
                        _networkInfoModel.Networks.RemoveAt(staleIndex);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"{nameof(NetworkInfoJob)}.{nameof(RunAsync)} failed: {exception}");
            }
            finally
            {
                // The first pass is over, whether or not it could read the interfaces: "not read yet" is for before that,
                // and a list that never leaves it shows "Reading…" for ever.
                _networkInfoModel.IsLoaded = true;
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                // WaitHandle.WaitOne returns immediately once cancellationToken fires, unlike
                // Thread.Sleep, so shutdown (e.g. F10 Quit) isn't stalled waiting out the interval.
                cancellationToken.WaitHandle.WaitOne(10000);
            }
        }
    }

    private void Update(NetworkInfo networkInfo, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"{nameof(NetworkInfoJob)}.{nameof(Update)} cancellation check failed: {exception}");
        }

        var oldNetworkInfo = _networkInfoModel.Networks.AsSnapshot().FirstOrDefault(d => d.Name == networkInfo.Name);
        if (oldNetworkInfo is null)
        {
            _networkInfoModel.Networks.Add(networkInfo);
            return;
        }

        var index = _networkInfoModel.Networks.IndexOf(oldNetworkInfo);
        if (index >= 0 && HasChanged(oldNetworkInfo, networkInfo))
        {
            _networkInfoModel.Networks.ReplaceAt(index, networkInfo);
        }
    }

    internal static bool HasChanged(NetworkInfo oldNetworkInfo, NetworkInfo newNetworkInfo)
    {
        return CreateSnapshot(oldNetworkInfo) != CreateSnapshot(newNetworkInfo);
    }

    internal static bool HasChanged(
        string? oldName,
        string? oldDescription,
        OperationalStatus? oldOperationalStatus,
        string? oldId,
        NetworkInterfaceType? oldNetworkInterfaceType,
        string? newName,
        string? newDescription,
        OperationalStatus? newOperationalStatus,
        string? newId,
        NetworkInterfaceType? newNetworkInterfaceType)
    {
        return new NetworkInfoSnapshot(oldName, oldDescription, oldOperationalStatus, oldId, oldNetworkInterfaceType)
            != new NetworkInfoSnapshot(newName, newDescription, newOperationalStatus, newId, newNetworkInterfaceType);
    }

    private static NetworkInfoSnapshot CreateSnapshot(NetworkInfo networkInfo)
    {
        return new NetworkInfoSnapshot(
            networkInfo.Name,
            networkInfo.NetworkInterface?.Description,
            networkInfo.NetworkInterface?.OperationalStatus,
            networkInfo.NetworkInterface?.Id,
            networkInfo.NetworkInterface?.NetworkInterfaceType);
    }
}
