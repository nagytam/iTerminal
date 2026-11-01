namespace iTerminal.Core.Services.Bluetooth;

/// <summary>One Bluetooth device BlueZ knows about: paired, nearby or both.</summary>
public sealed record BluetoothDevice(string Address, string Name, bool IsPaired, bool IsConnected);

/// <summary>The devices BlueZ knows about, or why they could not be read.</summary>
public sealed record BluetoothListing(IReadOnlyList<BluetoothDevice> Devices, string? Error);

/// <summary>What a connect, pair, scan or remove did. <see cref="Message"/> is the tool's own first line
/// of text when it failed, so the reason reaches the person as the tool reported it.</summary>
public sealed record BluetoothOutcome(bool Succeeded, string Message);

/// <summary>
/// Lists and changes Bluetooth devices through the host's Bluetooth stack. On Linux that is BlueZ, driven
/// through <c>bluetoothctl</c>; elsewhere <see cref="IsSupported"/> is false and every call reports why.
/// </summary>
public interface IBluetoothService
{
    /// <summary>False where this build has no Bluetooth backend (Windows for now).</summary>
    bool IsSupported { get; }

    /// <summary>The devices BlueZ knows: every paired device, every device seen recently, with their state.</summary>
    Task<BluetoothListing> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks for nearby devices for a few seconds. The listing afterwards shows what was found.</summary>
    Task<BluetoothOutcome> ScanAsync(CancellationToken cancellationToken = default);

    Task<BluetoothOutcome> ConnectAsync(string address, CancellationToken cancellationToken = default);

    Task<BluetoothOutcome> DisconnectAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Pairs with the device. A device that asks for a PIN is refused: this build has no agent to answer it.</summary>
    Task<BluetoothOutcome> PairAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Forgets the device: removes its pairing and its cached record.</summary>
    Task<BluetoothOutcome> RemoveAsync(string address, CancellationToken cancellationToken = default);
}
