using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Bluetooth;

/// <summary>
/// <see cref="IBluetoothService"/> over BlueZ's <c>bluetoothctl</c>, run one command at a time through
/// <see cref="IExternalProcessRunner"/>. Every call is a short process with its own timeout, so none of them
/// holds a key handler: the dialog runs them on its background operation and writes the result back in
/// <c>OnIdle</c>.
/// </summary>
public sealed class BluetoothService(IExternalProcessRunner processRunner) : IBluetoothService
{
    private const string Tool = "bluetoothctl";

    /// <summary>How long a scan listens. BlueZ stops discovery when the bluetoothctl that started it exits.</summary>
    public const int ScanSeconds = 8;

    /// <summary>How many <c>info</c> calls run at once while a listing is read.</summary>
    private const int InfoParallelism = 4;

    private const string UnsupportedReason = "Bluetooth devices are managed through BlueZ on Linux; this system has no Bluetooth backend.";

    public bool IsSupported => OperatingSystem.IsLinux();

    public async Task<BluetoothListing> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return new BluetoothListing([], UnsupportedReason);
        }

        var known = await processRunner.RunAsync(Tool, ["devices"], null, cancellationToken);
        if (BluetoothctlParser.ListingFailed(known))
        {
            return new BluetoothListing([], BluetoothctlParser.Reason(known));
        }

        // One "info" per device, a few at a time: a listing of a long-used adapter has dozens of devices, and every
        // change ends with a listing, so these run in parallel rather than one process after another.
        using var gate = new SemaphoreSlim(InfoParallelism);
        var devices = await Task.WhenAll(BluetoothctlParser.ParseDevices(known.StandardOutput).Select(async entry =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var info = await processRunner.RunAsync(Tool, ["info", entry.Address], null, cancellationToken);
                var (isPaired, isConnected) = BluetoothctlParser.ParseState(info.StandardOutput);
                return new BluetoothDevice(entry.Address, entry.Name, isPaired, isConnected);
            }
            finally
            {
                gate.Release();
            }
        }));

        // The connected ones first, then the paired ones, then by name: what the person is most likely to want is on top.
        var ordered = devices
            .OrderByDescending(device => device.IsConnected)
            .ThenByDescending(device => device.IsPaired)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new BluetoothListing(ordered, null);
    }

    public async Task<BluetoothOutcome> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return new BluetoothOutcome(false, UnsupportedReason);
        }

        var scan = await processRunner.RunAsync(Tool, ["--timeout", ScanSeconds.ToString(), "scan", "on"], null, cancellationToken);
        return Outcome(scan);
    }

    public Task<BluetoothOutcome> ConnectAsync(string address, CancellationToken cancellationToken = default) =>
        RunOnDeviceAsync("connect", address, cancellationToken);

    public Task<BluetoothOutcome> DisconnectAsync(string address, CancellationToken cancellationToken = default) =>
        RunOnDeviceAsync("disconnect", address, cancellationToken);

    public Task<BluetoothOutcome> PairAsync(string address, CancellationToken cancellationToken = default) =>
        RunOnDeviceAsync("pair", address, cancellationToken);

    public Task<BluetoothOutcome> RemoveAsync(string address, CancellationToken cancellationToken = default) =>
        RunOnDeviceAsync("remove", address, cancellationToken);

    private async Task<BluetoothOutcome> RunOnDeviceAsync(string command, string address, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            return new BluetoothOutcome(false, UnsupportedReason);
        }

        // The address is checked before it reaches the tool, so nothing but a MAC address can be passed on.
        if (!BluetoothctlParser.IsAddress(address))
        {
            return new BluetoothOutcome(false, $"Not a Bluetooth address: {address}");
        }

        var output = await processRunner.RunAsync(Tool, [command, address], null, cancellationToken);
        return Outcome(output);
    }

    private static BluetoothOutcome Outcome(ProcessOutput output) =>
        BluetoothctlParser.Failed(output)
            ? new BluetoothOutcome(false, BluetoothctlParser.Reason(output))
            : new BluetoothOutcome(true, string.Empty);
}
