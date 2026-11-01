using iConsole.Data;
using iTerminal.Core.Dialogs.Bluetooth;

namespace iTerminal.Core.Commands;

public class BluetoothDeviceManagerDialogCommand(BluetoothDeviceManagerDialog bluetoothDeviceManagerDialog) : ShowDialogCommand<BluetoothDeviceManagerDialog>(bluetoothDeviceManagerDialog)
{
    public override string Title => "Bluetooth devices";
    public override string Description => "Scan for, connect, pair and forget Bluetooth devices.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.J, true, false, true);
}
