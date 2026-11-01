using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Services.Bluetooth;

namespace iTerminal.Core.Dialogs.Bluetooth;

/// <summary>
/// Lists the Bluetooth devices BlueZ knows and changes them: scan, connect or disconnect, pair, forget.
/// Every change runs on one background operation at a time. A worker never touches the control tree: it
/// leaves its result (the new rows, the status text) for the UI thread, which takes them in <see cref="OnIdle"/>
/// or on the next key.
/// </summary>
public class BluetoothDeviceManagerDialog : Dialog
{
    private const int NameColumnWidth = 30;

    private readonly IBluetoothService _bluetooth;
    private readonly ConcurrentObservableCollection<BluetoothDevice> _devices = [];
    private readonly ListBox<BluetoothDevice> _listBox;
    private readonly Label _status;
    private readonly CloseButton _closeButton;

    // Written by the operation, read on the UI thread.
    private volatile IReadOnlyList<BluetoothDevice>? _pendingDevices;
    private volatile string _statusText = string.Empty;
    private volatile bool _loaded;
    private volatile bool _working;
    private volatile int _operationId;
    private Task _operation = Task.CompletedTask;

    // Cancelled when the dialog closes, so a scan does not run on for a dialog nobody is looking at.
    private CancellationTokenSource _cancellation = new();

    // The highlighted device, by address. A refresh can reorder the rows, so the cursor is put back on this device.
    private string? _selectedAddress;

    // Set and cleared only on the UI thread (key handling).
    private bool _confirmingRemove;

    public BluetoothDeviceManagerDialog(IBluetoothService bluetooth, IThemeService themeService)
        : base(nameof(BluetoothDeviceManagerDialog), themeService)
    {
        _bluetooth = bluetooth;
        Title = UiText.Get("Dialog.BluetoothDeviceManager", "Bluetooth devices");

        var listModel = new ListBoxModel<BluetoothDevice>(_devices)
        {
            TransformItem = TransformItem,
        };
        _listBox = new ListBox<BluetoothDevice>(listModel, themeService)
        {
            EmptyText = UiText.Get("Status.Bluetooth.Reading", "Reading the Bluetooth devices…"),
        };
        _status = new Label(string.Empty, themeService);
        _closeButton = new CloseButton(themeService);

        AddControl(_listBox);
        AddControl(_status);
        AddControl(_closeButton);

        if (!_bluetooth.IsSupported)
        {
            _statusText = UiText.Get("Status.Bluetooth.Unsupported", "Bluetooth devices are managed through BlueZ on Linux; this system has no Bluetooth backend.");
            _loaded = true;
        }
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();

        // The dialog takes focus itself, and a dialog with focus passes arrow keys to no child: the list gets it.
        _listBox.SetFocus();
        if (_cancellation.IsCancellationRequested)
        {
            _cancellation = new CancellationTokenSource();
        }

        if (_bluetooth.IsSupported)
        {
            Read();
        }
    }

    public override void Close()
    {
        _cancellation.Cancel();
        base.Close();
    }

    public override void OnIdle()
    {
        ApplyPendingDevices();
        _status.Text = _statusText;
        _listBox.EmptyText = _loaded || !_bluetooth.IsSupported
            ? UiText.Get("Empty.BluetoothDevices", "No Bluetooth devices known. Press S to scan for nearby ones.")
            : UiText.Get("Status.Bluetooth.Reading", "Reading the Bluetooth devices…");
        base.OnIdle();
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 5);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _status.Position = new Position(1, ViewSize.Height - 3);
        _status.ViewSize = new Size(ViewSize.Width - 2, 1);

        _closeButton.Position = new Position(1, ViewSize.Height - 2);
        _closeButton.ViewSize = new Size(ViewSize.Width - 2, 1);

        base.OnInitializeLayout();
    }

    /// <summary>One device as a row: name in a fixed column, then the address, then its state.</summary>
    internal static string FormatRow(BluetoothDevice device, string stateText)
    {
        var name = (device.Name ?? string.Empty).Trim().Shrink(NameColumnWidth - 1) ?? string.Empty;
        return name.PadRight(NameColumnWidth) + " " + device.Address.PadRight(17) + "  " + stateText;
    }

    private ListItem<BluetoothDevice> TransformItem(BluetoothDevice device, CancellationToken cancellationToken)
    {
        var state = device.IsConnected
            ? UiText.Get("State.Bluetooth.Connected", "connected")
            : device.IsPaired
                ? UiText.Get("State.Bluetooth.Paired", "paired")
                : UiText.Get("State.Bluetooth.Nearby", "nearby");
        return new ListItem<BluetoothDevice>(device)
        {
            Value = FormatRow(device, state),
            IsEnabled = true,
        };
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("C", UiText.Get("DialogHint.Bluetooth.ConnectDisconnect", "Connect or disconnect the device"));
        yield return ("P", UiText.Get("DialogHint.Bluetooth.Pair", "Pair with the device"));
        yield return ("S", UiText.Get("DialogHint.Bluetooth.Scan", "Scan for nearby devices"));
        yield return ("R", UiText.Get("DialogHint.Bluetooth.Refresh", "Read the devices again"));
        yield return ("Delete", UiText.Get("DialogHint.Bluetooth.Remove", "Forget the device"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_confirmingRemove)
        {
            // Any key answers the prompt: y removes, anything else (Esc included) backs out of the prompt only.
            _confirmingRemove = false;
            var device = SelectedDevice();
            if (keyEvent.KeyChar is ('y' or 'Y') && device is not null && !_working)
            {
                Remove(device);
            }
            else
            {
                _statusText = string.Empty;
            }

            return true;
        }

        if (!keyEvent.Control && !keyEvent.Alt)
        {
            // Keys the dialog answers itself. Arrow keys never reach here: the focused list runs them as commands.
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.C:
                    ToggleConnection();
                    return true;
                case ConsoleKey.P:
                    Pair();
                    return true;
                case ConsoleKey.S:
                    Scan();
                    return true;
                case ConsoleKey.R:
                    Read();
                    return true;
                case ConsoleKey.Delete:
                    AskToRemove();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// The device under the cursor, now. The cursor is the highlight; the list's own <c>IsSelected</c> marks are a
    /// different thing (what Space toggles), so they are not used here. Rows a worker left are taken in first, and
    /// the highlight is read afterwards: it is then the device the cursor was put back on, or the first row.
    /// </summary>
    private BluetoothDevice? SelectedDevice()
    {
        _listBox.UpdateModel();
        var before = HighlightedDevice();
        if (_pendingDevices is null)
        {
            return before;
        }

        ApplyPendingDevices();
        return HighlightedDevice();
    }

    private BluetoothDevice? HighlightedDevice()
    {
        var items = _listBox.ListModel.ListItems;
        var cursor = (int)_listBox.ScrollableModel.CursorPosition.Y;
        var device = cursor >= 0 && cursor < items.Count ? items[cursor].Item : null;
        if (device is not null)
        {
            _selectedAddress = device.Address;
        }

        return device;
    }

    /// <summary>
    /// Takes the rows a worker left, on the UI thread, and puts the cursor back on the device that was highlighted.
    /// </summary>
    private void ApplyPendingDevices()
    {
        var pending = _pendingDevices;
        if (pending is null)
        {
            return;
        }

        _pendingDevices = null;
        if (_selectedAddress is null || pending.All(device => device.Address != _selectedAddress))
        {
            _selectedAddress = pending.FirstOrDefault()?.Address;
        }

        _devices.ReplaceAll(pending.ToList());

        // UpdateModel, not the model alone: it also sizes the scroll range, which the cursor is clamped to.
        _listBox.UpdateModel();
        var index = _listBox.ListModel.ListItems.ToList().FindIndex(item => item.Item.Address == _selectedAddress);
        _listBox.ScrollableModel.CursorPosition.Y = Math.Max(index, 0);
    }

    private void Read()
    {
        Run(
            UiText.Get("Status.Bluetooth.Reading", "Reading the Bluetooth devices…"),
            async cancellationToken =>
            {
                try
                {
                    var listing = await _bluetooth.ListAsync(cancellationToken);
                    _pendingDevices = listing.Devices;
                    return listing.Error ?? string.Empty;
                }
                finally
                {
                    _loaded = true;
                }
            });
    }

    private void Scan()
    {
        Run(
            UiText.Get("Status.Bluetooth.Scanning", "Scanning for nearby devices…"),
            async cancellationToken =>
            {
                var outcome = await _bluetooth.ScanAsync(cancellationToken);
                var listing = await _bluetooth.ListAsync(cancellationToken);
                _pendingDevices = listing.Devices;
                if (!outcome.Succeeded)
                {
                    return outcome.Message;
                }

                return listing.Error ?? string.Format(
                    UiText.Get("Status.Bluetooth.Scanned", "Scan finished: {0} devices known."),
                    listing.Devices.Count);
            });
    }

    private void ToggleConnection()
    {
        var device = SelectedDevice();
        if (device is null)
        {
            return;
        }

        if (device.IsConnected)
        {
            Act(
                string.Format(UiText.Get("Status.Bluetooth.Disconnecting", "Disconnecting {0}…"), device.Name),
                cancellationToken => _bluetooth.DisconnectAsync(device.Address, cancellationToken),
                string.Format(UiText.Get("Status.Bluetooth.Disconnected", "Disconnected {0}."), device.Name));
        }
        else
        {
            Act(
                string.Format(UiText.Get("Status.Bluetooth.Connecting", "Connecting to {0}…"), device.Name),
                cancellationToken => _bluetooth.ConnectAsync(device.Address, cancellationToken),
                string.Format(UiText.Get("Status.Bluetooth.Connected", "Connected to {0}."), device.Name));
        }
    }

    private void Pair()
    {
        var device = SelectedDevice();
        if (device is null)
        {
            return;
        }

        if (device.IsPaired)
        {
            _statusText = string.Format(UiText.Get("Status.Bluetooth.AlreadyPaired", "{0} is already paired."), device.Name);
            return;
        }

        Act(
            string.Format(UiText.Get("Status.Bluetooth.Pairing", "Pairing with {0}…"), device.Name),
            cancellationToken => _bluetooth.PairAsync(device.Address, cancellationToken),
            string.Format(UiText.Get("Status.Bluetooth.Paired", "Paired with {0}."), device.Name));
    }

    private void AskToRemove()
    {
        var device = SelectedDevice();
        if (device is null || _working)
        {
            return;
        }

        _confirmingRemove = true;
        _statusText = string.Format(UiText.Get("Prompt.Bluetooth.Remove", "Remove \"{0}\" from this computer? (y/n)"), device.Name);
    }

    private void Remove(BluetoothDevice device)
    {
        Act(
            string.Format(UiText.Get("Status.Bluetooth.Removing", "Removing {0}…"), device.Name),
            cancellationToken => _bluetooth.RemoveAsync(device.Address, cancellationToken),
            string.Format(UiText.Get("Status.Bluetooth.Removed", "Removed {0}."), device.Name));
    }

    /// <summary>Runs one change, reads the devices again and reports the outcome in the status line.</summary>
    private void Act(string busyText, Func<CancellationToken, Task<BluetoothOutcome>> change, string successText)
    {
        Run(busyText, async cancellationToken =>
        {
            var outcome = await change(cancellationToken);
            var listing = await _bluetooth.ListAsync(cancellationToken);
            _pendingDevices = listing.Devices;
            return outcome.Succeeded ? successText : outcome.Message;
        });
    }

    /// <summary>
    /// Starts one background operation. A second one is refused while the first runs, so two changes never race
    /// each other; the refusal is said in the status line. Runs on the UI thread only.
    /// </summary>
    private void Run(string busyText, Func<CancellationToken, Task<string>> work)
    {
        if (_working)
        {
            _statusText = UiText.Get("Status.Bluetooth.Busy", "Still working on the last change; wait for it to finish.");
            return;
        }

        // The highlight is read before the operation can change the rows under it.
        SelectedDevice();
        _working = true;
        _statusText = busyText;
        var operationId = ++_operationId;
        _operation = RunAsync(operationId, _cancellation.Token, work);
    }

    private async Task RunAsync(int operationId, CancellationToken cancellationToken, Func<CancellationToken, Task<string>> work)
    {
        string result;
        try
        {
            result = await work(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = string.Empty;
        }
        catch (Exception exception)
        {
            result = string.Format(UiText.Get("Status.Bluetooth.Error", "Bluetooth failed: {0}"), exception.Message);
        }
        finally
        {
            // Only the operation that is still the current one may release the flag: a stale finish must not clear a newer one.
            if (operationId == _operationId)
            {
                _working = false;
            }
        }

        if (operationId == _operationId)
        {
            _statusText = result;
        }
    }

    internal Task OperationForTest => _operation;

    internal ListBox<BluetoothDevice> ListBoxForTest => _listBox;

    internal string StatusForTest => _statusText;

    internal bool ConfirmingRemoveForTest => _confirmingRemove;
}
