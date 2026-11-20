using iTerminal.Core.Dialogs.Bluetooth;
using iTerminal.Core.Services.Bluetooth;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class BluetoothDeviceManagerDialogTest
{
    private const string Headphones = "50:C2:ED:36:8D:55";
    private const string Keyboard = "66:77:88:99:AA:BB";

    [TestMethod]
    [Timeout(60_000)]
    public async Task OpeningReadsTheDevicesAndShowsTheirState()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Headphones, "Headphones", IsPaired: true, IsConnected: true));
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());

        await ShowAsync(dialog);
        await dialog.OperationForTest;
        dialog.OnIdle();

        Assert.AreEqual("Bluetooth devices", dialog.Title);
        AssertScreenContains(Render(dialog), "Headphones");
        AssertScreenContains(Render(dialog), "connected");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AnEmptyListSaysHowToScan()
    {
        var bluetooth = new RecordingBluetooth();
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());

        await ShowAsync(dialog);
        await dialog.OperationForTest;
        dialog.OnIdle();

        AssertScreenContains(Render(dialog), "Press S to scan");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task WithoutABackendTheDialogSaysSoAndReadsNothing()
    {
        var bluetooth = new RecordingBluetooth { IsSupported = false };
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());

        await ShowAsync(dialog);
        dialog.OnIdle();

        AssertScreenContains(Render(dialog), "BlueZ");
        Assert.AreEqual(0, bluetooth.ListCount);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AReadOnlyBackendListsTheDevicesAndSaysNothingCanBeChanged()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Headphones, "Headphones", IsPaired: true, IsConnected: false)) { CanChange = false };
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;
        dialog.OnIdle();

        AssertScreenContains(Render(dialog), "Headphones");
        CollectionAssert.AreEqual(new[] { "R" }, dialog.GetKeyHints().Select(hint => hint.Key).ToArray(), "Only the refresh key is offered.");

        foreach (ConsoleKey key in new[] { ConsoleKey.C, ConsoleKey.P, ConsoleKey.S, ConsoleKey.Delete })
        {
            await dialog.HandleKeyEventAsync(Key(key));
            await dialog.OperationForTest;
            StringAssert.Contains(dialog.StatusForTest, "can only list");
            Assert.IsFalse(dialog.ConfirmingRemoveForTest, "Delete must not ask whether to remove.");
        }

        Assert.AreEqual(0, bluetooth.Connected.Count + bluetooth.Paired.Count + bluetooth.Removed.Count);
        Assert.AreEqual(1, bluetooth.ListCount, "Only the first read listed; the refused keys read nothing.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AReadOnlyBackendWithNoDevicesDoesNotAdvertiseScanning()
    {
        var dialog = new BluetoothDeviceManagerDialog(new RecordingBluetooth { CanChange = false }, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;
        dialog.OnIdle();

        AssertScreenContains(Render(dialog), "No paired Bluetooth devices");
        Assert.IsFalse(Render(dialog).ToString()!.Contains("Press S"));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task COnAConnectedDeviceDisconnectsIt()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Headphones, "Headphones", IsPaired: true, IsConnected: true));
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.C));
        await dialog.OperationForTest;

        CollectionAssert.AreEqual(new[] { Headphones }, bluetooth.Disconnected.ToArray());
        Assert.AreEqual("Disconnected Headphones.", dialog.StatusForTest);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task COnANearbyDeviceConnectsIt()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Keyboard, "Keyboard", IsPaired: false, IsConnected: false));
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.C));
        await dialog.OperationForTest;

        CollectionAssert.AreEqual(new[] { Keyboard }, bluetooth.Connected.ToArray());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ARefreshKeepsTheHighlightedDevice()
    {
        var headphones = new BluetoothDevice(Headphones, "Headphones", IsPaired: true, IsConnected: false);
        var keyboard = new BluetoothDevice(Keyboard, "Keyboard", IsPaired: false, IsConnected: false);
        var bluetooth = new RecordingBluetooth(headphones, keyboard);
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;

        dialog.OnIdle(); // the rows are shown, as they are in the app between two keys
        dialog.ListBoxForTest.ScrollableModel.CursorPosition.Y = 1; // the keyboard, the second row

        // The next listing puts the keyboard first; the cursor must follow the keyboard, not stay on row two.
        bluetooth.Devices = [keyboard, headphones];
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.R));
        await dialog.OperationForTest;
        dialog.OnIdle();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.C));
        await dialog.OperationForTest;

        CollectionAssert.AreEqual(new[] { Keyboard }, bluetooth.Connected.ToArray());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task PairingAPairedDeviceIsRefusedWithAReason()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Headphones, "Headphones", IsPaired: true, IsConnected: false));
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.P));

        Assert.AreEqual("Headphones is already paired.", dialog.StatusForTest);
        Assert.AreEqual(0, bluetooth.Paired.Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task RemoveAsksFirstAndYRemoves()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Headphones, "Headphones", IsPaired: true, IsConnected: false));
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        Assert.IsTrue(dialog.ConfirmingRemoveForTest);
        Assert.AreEqual(0, bluetooth.Removed.Count, "Nothing is forgotten before the answer.");

        await dialog.HandleKeyEventAsync(Char('y'));
        await dialog.OperationForTest;

        CollectionAssert.AreEqual(new[] { Headphones }, bluetooth.Removed.ToArray());
        Assert.IsFalse(dialog.ConfirmingRemoveForTest);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task RemoveAnsweredWithAnythingElseKeepsTheDevice()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Headphones, "Headphones", IsPaired: true, IsConnected: false));
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        await dialog.HandleKeyEventAsync(Char('n'));

        Assert.IsFalse(dialog.ConfirmingRemoveForTest);
        Assert.AreEqual(0, bluetooth.Removed.Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task EscapeBacksOutOfTheRemovePromptBeforeItClosesTheDialog()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Headphones, "Headphones", IsPaired: true, IsConnected: false));
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        var root = await ShowAsync(dialog);
        await dialog.OperationForTest;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(dialog.ConfirmingRemoveForTest);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "The first Escape only cancels the prompt.");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AFailedChangeShowsTheToolsReason()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Keyboard, "Keyboard", IsPaired: false, IsConnected: false))
        {
            ConnectOutcome = new BluetoothOutcome(false, "Failed to connect: org.bluez.Error.Failed"),
        };
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.C));
        await dialog.OperationForTest;
        dialog.OnIdle();

        AssertScreenContains(Render(dialog), "Failed to connect");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AChangeRefusedWhileAnotherRunsSaysSo()
    {
        var gate = new TaskCompletionSource();
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Keyboard, "Keyboard", IsPaired: false, IsConnected: false))
        {
            ConnectGate = gate.Task,
        };
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;
        dialog.OnIdle();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.C));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.S));

        StringAssert.Contains(dialog.StatusForTest, "Still working");
        gate.SetResult();
        await dialog.OperationForTest;
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ClosingTheDialogCancelsWhatItIsDoing()
    {
        var bluetooth = new RecordingBluetooth(new BluetoothDevice(Keyboard, "Keyboard", IsPaired: false, IsConnected: false))
        {
            ConnectGate = Task.Delay(Timeout.Infinite),
        };
        var dialog = new BluetoothDeviceManagerDialog(bluetooth, TestTheme());
        await ShowAsync(dialog);
        await dialog.OperationForTest;
        dialog.OnIdle();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.C));

        dialog.Close();

        await dialog.OperationForTest;
        Assert.IsTrue(bluetooth.ConnectWasCancelled, "The connect was cancelled with the dialog.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void TheRowKeepsItsColumnsWhenTheNameIsLong()
    {
        var row = BluetoothDeviceManagerDialog.FormatRow(new BluetoothDevice(Keyboard, new string('x', 80), false, false), "nearby");

        Assert.AreEqual(Keyboard, row.Substring(31, 17));
        Assert.IsTrue(row.EndsWith("nearby"));
    }

    /// <summary>A Bluetooth service that records what the dialog asks for and answers from fields.</summary>
    private sealed class RecordingBluetooth(params BluetoothDevice[] devices) : IBluetoothService
    {
        public BluetoothDevice[] Devices { get; set; } = devices;

        private readonly object _lock = new();
        private readonly List<string> _connected = [];
        private readonly List<string> _disconnected = [];
        private readonly List<string> _paired = [];
        private readonly List<string> _removed = [];
        private int _listCount;

        public bool IsSupported { get; init; } = true;

        public bool CanChange { get; init; } = true;

        public BluetoothOutcome ConnectOutcome { get; init; } = new(true, string.Empty);

        /// <summary>When set, a connect waits for this before it answers, so a test can hold an operation open.</summary>
        public Task? ConnectGate { get; init; }

        public bool ConnectWasCancelled { get; private set; }

        public IReadOnlyList<string> Connected => Snapshot(_connected);
        public IReadOnlyList<string> Disconnected => Snapshot(_disconnected);
        public IReadOnlyList<string> Paired => Snapshot(_paired);
        public IReadOnlyList<string> Removed => Snapshot(_removed);

        public int ListCount
        {
            get
            {
                lock (_lock)
                {
                    return _listCount;
                }
            }
        }

        public Task<BluetoothListing> ListAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _listCount++;
            }

            return Task.FromResult(new BluetoothListing(Devices, null));
        }

        public Task<BluetoothOutcome> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BluetoothOutcome(true, string.Empty));

        public async Task<BluetoothOutcome> ConnectAsync(string address, CancellationToken cancellationToken = default)
        {
            Record(_connected, address);
            if (ConnectGate is not null)
            {
                try
                {
                    await ConnectGate.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ConnectWasCancelled = true;
                    throw;
                }
            }

            return ConnectOutcome;
        }

        public Task<BluetoothOutcome> DisconnectAsync(string address, CancellationToken cancellationToken = default)
        {
            Record(_disconnected, address);
            return Task.FromResult(new BluetoothOutcome(true, string.Empty));
        }

        public Task<BluetoothOutcome> PairAsync(string address, CancellationToken cancellationToken = default)
        {
            Record(_paired, address);
            return Task.FromResult(new BluetoothOutcome(true, string.Empty));
        }

        public Task<BluetoothOutcome> RemoveAsync(string address, CancellationToken cancellationToken = default)
        {
            Record(_removed, address);
            return Task.FromResult(new BluetoothOutcome(true, string.Empty));
        }

        private void Record(List<string> list, string address)
        {
            lock (_lock)
            {
                list.Add(address);
            }
        }

        private IReadOnlyList<string> Snapshot(List<string> list)
        {
            lock (_lock)
            {
                return [.. list];
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FormatRow_WithAShortAddress_KeepsTheLastThreeBytesAndTheWholeState()
    {
        var row = BluetoothDeviceManagerDialog.FormatRow(new BluetoothDevice("00:11:22:33:44:55", "Sandbox Headphones", false, false), "connected", nameWidth: 12, shortAddress: true);

        Assert.AreEqual("Sand...ones  33:44:55  connected", row);
        Assert.IsTrue(row.EndsWith("connected", StringComparison.Ordinal));
        Assert.IsFalse(row.Contains("00:11", StringComparison.Ordinal), row);
    }
}
