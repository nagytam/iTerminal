using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// iPhone Browser: lists iOS devices reachable over USB and, on selection, mounts the chosen device
/// as an <c>iphone://&lt;udid&gt;/</c> drive in the focused file panel (via
/// <see cref="IContentModelService.SetUrlAsync(Url)"/>). Device enumeration runs off the render thread
/// through <see cref="IIPhoneDeviceService"/>; the key handler never blocks on native calls.
/// </summary>
public class IPhoneBrowserDialog : Dialog
{
    private readonly IIPhoneDeviceService _deviceService;
    private readonly IContentModelService _contentModelService;

    private readonly ConcurrentObservableCollection<IPhoneDeviceInfo> _devices = new();
    private readonly ListBox<IPhoneDeviceInfo> _listBox;

    private bool _scanning;
    private string _status = string.Empty;
    private bool _statusIsError;

    public IPhoneBrowserDialog(
        IIPhoneDeviceService deviceService,
        IContentModelService contentModelService,
        IThemeService themeService)
        : base(nameof(IPhoneBrowserDialog), themeService)
    {
        _deviceService = deviceService;
        _contentModelService = contentModelService;
        Title = "iPhone Browser";

        var listModel = new ListBoxModel<IPhoneDeviceInfo>(_devices) { TransformItem = TransformItem };
        _listBox = new ListBox<IPhoneDeviceInfo>(listModel, themeService);
        AddControl(_listBox);
    }

    private static ListItem<IPhoneDeviceInfo> TransformItem(IPhoneDeviceInfo item, CancellationToken cancellationToken) =>
        new(item) { Value = item.DisplayName, IsEnabled = true };

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        Rescan();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.6);
        }

        _listBox.Position = new Position(1, 2);
        _listBox.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 4);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        base.OnInitializeLayout();
    }

    internal const string KeyHints = "r:rescan  Enter:open  Esc:close";
    internal const string NoDevicesText = " No iOS devices found. Connect an iPhone, unlock it and trust this computer — then r rescans.";

    private void Rescan()
    {
        // One scan at a time: a second 'r' used to start another enumeration over the first.
        if (_scanning)
        {
            return;
        }

        _scanning = true;
        _status = string.Empty;
        _statusIsError = false;
        _listBox.EmptyText = " Looking for devices…";
        StateHasChanged();

        _ = Task.Run(() =>
        {
            try
            {
                var devices = _deviceService.GetDevices();
                _devices.ReplaceAll([.. devices]);
                _listBox.UpdateModel();
                // Said in the list, where the devices would be; the footer keeps the keys, and with no
                // device found "r:rescan" is the one the user needs.
                _listBox.EmptyText = NoDevicesText;
            }
            catch (Exception exception)
            {
                _status = $"Device scan error: {exception.Message}";
                _statusIsError = true;
                _listBox.EmptyText = string.Empty;
            }
            finally
            {
                _scanning = false;
                StateHasChanged();
            }
        });
    }

    /// <summary>
    /// The device under the cursor, read from the device list itself. The list box's own items are a
    /// projection of it that the scan refreshes a moment later (<c>UpdateModel</c>), and Enter pressed
    /// in between found no device and did nothing.
    /// </summary>
    private IPhoneDeviceInfo? SelectedDevice
    {
        get
        {
            var devices = _devices.AsSnapshot();
            var index = (int)_listBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < devices.Count ? devices[index] : null;
        }
    }

    private async Task ConnectSelectedAsync()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        _status = $"Opening {device.Name}...";
        _statusIsError = false;
        StateHasChanged();

        try
        {
            await _contentModelService.SetUrlAsync(new Url($"{Container.IPHONE}{device.Udid}/"));
            Close();
        }
        catch (Exception exception)
        {
            _status = $"Could not open {device.Name}: {exception.Message}";
            _statusIsError = true;
            StateHasChanged();
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", "Connect to the chosen device");
        yield return ("R", "Search for devices again");
        yield return ("Esc", "Close");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt)
        {
            Close();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter)
        {
            await ConnectSelectedAsync();
            return true;
        }

        if (char.ToLowerInvariant(keyEvent.KeyChar) == 'r')
        {
            Rescan();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var ix = (int)AbsolutePosition.X + 1;
        var iy = (int)AbsolutePosition.Y + 1;
        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        var scanning = _scanning ? "  (scanning…)" : string.Empty;
        WriteLine(buffer, ix, iy, iw, $"Connected devices{scanning}", Color.Cyan, colorScheme.Background);

        var fg = _statusIsError ? colorScheme.Error : colorScheme.Foreground;
        if (_status.Length == 0)
        {
            WriteLine(buffer, ix, iy + ih - 1, iw, KeyHints.FitHints(iw), colorScheme.Foreground, colorScheme.Background);
        }
        else
        {
            // The status first, the keys after it — a status used to replace them.
            var status = _status.Length + 4 < iw ? _status : _status[..Math.Max(0, iw)];
            WriteLine(buffer, ix, iy + ih - 1, status.Length, status, fg, colorScheme.Background);
            WriteLine(buffer, ix + status.Length, iy + ih - 1, iw - status.Length, "  │ " + KeyHints.FitHints(iw - status.Length - 4), colorScheme.Foreground, colorScheme.Background);
        }
    }

    private void WriteLine(IBuffer buffer, int x, int y, int w, string text, Color foreground, Color background)
    {
        if (w <= 0)
        {
            return;
        }
        text ??= string.Empty;
        text = text.Length > w ? text[..w] : text.PadRight(w);
        buffer.Write(ZOrder, new Position(x, y), new Size(w, 1), new ConxelString(text, foreground, background));
    }

    #region Test seams

    internal IReadOnlyList<IPhoneDeviceInfo> DevicesForTest => [.. _devices.AsSnapshot()];
    internal bool IsScanningForTest => _scanning;
    internal string StatusForTest => _status;
    internal void SelectRowForTest(int index) => _listBox.ScrollableModel.CursorPosition.Y = index;
    internal Task ConnectSelectedAsyncForTest() => ConnectSelectedAsync();

    #endregion
}
