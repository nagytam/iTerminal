using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls;
using iTerminal.Core.Data;
using iTerminal.Core.Model;
using iTerminal.Core.Services.Drives;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Alt+F9: the drives, what is known about the one under the cursor, and what can be done to it —
/// E safely ejects it, F formats it, M mounts or unmounts a disk image, B reads BitLocker again.
///
/// <list type="bullet">
/// <item>Facts about a drive (label, file system, space, BitLocker) are read on a background task
/// when the cursor arrives on it: a card reader or a locked volume can take seconds, and
/// <c>Draw</c> and the key handler must not wait for it.</item>
/// <item>An action is a task too, and the dialog says so ("Ejecting E:...") until it ends; the
/// outcome is a toast as well as the status line, since the dialog may be closed by then.</item>
/// <item>Format asks for the drive letter back in a dialog of its own and is refused outright for
/// the system drive and for any drive a panel, or this program, is using.</item>
/// </list>
/// </summary>
public class DiskManagerDialog : Dialog
{
    private const int DetailRows = 7;

    private TaskManager _taskManager { get; set; }
    private DriveInfoModel _driveInfoModel { get; set; }
    private ListBox<Drive> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }
    private readonly InfoLines _details;
    private readonly IDriveActionsService? _driveActions;
    private readonly IContentModelService? _contentModelService;
    private readonly INotificationService? _notifications;
    private readonly FormatDriveDialog? _formatDialog;
    private readonly DiskImageDialog? _imageDialog;

    private string _shownRoot = string.Empty;
    private long _detailsGeneration;
    private IReadOnlyList<InfoLines.Line> _detailLines = [];
    private string _status = string.Empty;
    private bool _statusIsError;
    private bool _isBusy;

    public DiskManagerDialog(
        TaskManager taskManager,
        DriveInfoModel driveInfoModel,
        IThemeService themeService,
        IDriveActionsService? driveActions = null,
        IContentModelService? contentModelService = null,
        INotificationService? notifications = null,
        FormatDriveDialog? formatDialog = null,
        DiskImageDialog? imageDialog = null)
        : base(nameof(DiskManagerDialog), themeService)
    {
        // Support reflection-based instantiation in unit tests where dependencies
        // may be provided as null. Use simple defaults to avoid NullReferenceException
        // during construction while preserving normal DI behavior when non-null
        // instances are supplied.
        _taskManager = taskManager ?? new TaskManager();
        _driveInfoModel = driveInfoModel ?? new DriveInfoModel();
        _driveActions = driveActions;
        _contentModelService = contentModelService;
        _notifications = notifications;
        _formatDialog = formatDialog;
        _imageDialog = imageDialog;
        Title = "Disk manager";
        var listModel = new ListBoxModel<Drive>(_driveInfoModel.Drives)
        {
            TransformItem = TransformItem
        };
        _listBox = new ListBox<Drive>(listModel, themeService)
        {
            EmptyText = ReadingText,
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };
        _details = new InfoLines(themeService);

        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_details);
    }

    private const string ReadingText = " Reading the drives…";
    private const string NoneText = " No drives found.";

    /// <summary>The drive under the cursor — the row, not the selection flag only Enter sets.</summary>
    public Drive? SelectedDrive
    {
        get
        {
            var listItems = _listBox.ListModel.ListItems;
            var index = (int)_listBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < listItems.Count ? listItems[index].Item : null;
        }
    }

    /// <summary>The facts shown for the drive under the cursor, as text, for a test to read.</summary>
    public string DetailsText => _details.Text;

    /// <summary>The last status line.</summary>
    public string Status => _status;

    /// <summary>The action or the details read started last; it exists so a test can wait for it.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    public override void OnIdle()
    {
        _listBox.EmptyText = _driveInfoModel.IsLoaded ? NoneText : ReadingText;
        FollowCursor();
        base.OnIdle();
    }

    private ListItem<Drive> TransformItem(Drive item, CancellationToken cancellationToken)
    {
        var transformedItem = new ListItem<Drive>(item)
        {
            Value = item.Name,
            IsEnabled = item.IsEnabled
        };
        return transformedItem;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        var listHeight = Math.Max(1, ViewSize.Height - 2 - DetailRows);
        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(ViewSize.Width - 2, listHeight);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(ViewSize.Width - 1, 1);
        _scrollBar.ViewSize = new Size(1, listHeight);

        _details.Position = new Position(2, 1 + listHeight);
        _details.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), DetailRows);
    }

    // ---- what is known about the drive under the cursor -----------------------------------------

    /// <summary>Starts reading the facts when the cursor arrived on another drive.</summary>
    private void FollowCursor()
    {
        var drive = SelectedDrive;
        var root = drive is null ? string.Empty : RootOf(drive);
        if (string.Equals(root, _shownRoot, StringComparison.Ordinal))
        {
            _details.Lines = BuildLines();
            return;
        }

        _shownRoot = root;
        var generation = Interlocked.Increment(ref _detailsGeneration);
        _detailLines = drive is null ? [] : [new InfoLines.Line("Reading...")];
        _details.Lines = BuildLines();
        if (drive is null)
        {
            return;
        }

        PendingOperation = Task.Run(async () =>
        {
            var lines = await ReadDetailsAsync(drive, root);
            // Only the drive still under the cursor may write: a slow read must not paint over a
            // faster one that belongs to the row the user moved to.
            if (Interlocked.Read(ref _detailsGeneration) == generation)
            {
                _detailLines = lines;
                StateHasChanged();
            }
        });
    }

    private async Task<IReadOnlyList<InfoLines.Line>> ReadDetailsAsync(Drive drive, string root)
    {
        var lines = new List<InfoLines.Line>();
        var info = drive.DriveInfo;
        if (info is null)
        {
            lines.Add(new InfoLines.Line($"Drive       {root}"));
            lines.Add(new InfoLines.Line("Type        Memory drive (lives in this program and is saved to its database)"));
            return lines;
        }

        try
        {
            if (!info.IsReady)
            {
                lines.Add(new InfoLines.Line($"Drive       {root}"));
                lines.Add(new InfoLines.Line($"Type        {info.DriveType}"));
                lines.Add(new InfoLines.Line("State       Not ready (no media, or locked)"));
                return lines;
            }

            var label = string.IsNullOrEmpty(info.VolumeLabel) ? "(no label)" : info.VolumeLabel;
            lines.Add(new InfoLines.Line($"Drive       {root}   {label}"));
            lines.Add(new InfoLines.Line($"Type        {info.DriveType}   {info.DriveFormat}"));
            lines.Add(new InfoLines.Line($"Space       {FormatSize(info.AvailableFreeSpace)} free of {FormatSize(info.TotalSize)}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lines.Add(new InfoLines.Line($"Drive       {root}"));
            lines.Add(new InfoLines.Line($"State       {exception.Message}"));
        }

        if (_driveActions is not null)
        {
            var bitLocker = await _driveActions.GetBitLockerStatusAsync(root);
            var color = bitLocker.State switch
            {
                BitLockerState.On => (Color?)Color.Green,
                BitLockerState.Unavailable or BitLockerState.Unknown => Color.Yellow,
                _ => null,
            };
            lines.Add(new InfoLines.Line($"BitLocker   {bitLocker.Text}", color));
        }

        return lines;
    }

    private IReadOnlyList<InfoLines.Line> BuildLines()
    {
        var lines = new List<InfoLines.Line>(_detailLines);
        while (lines.Count < DetailRows - 3)
        {
            lines.Add(new InfoLines.Line(string.Empty));
        }

        lines.Add(new InfoLines.Line(string.Empty));
        lines.Add(_status.Length > 0
            ? new InfoLines.Line(_status, _statusIsError ? Color.Red : Color.Green)
            : new InfoLines.Line(_isBusy ? "Working..." : string.Empty));
        return lines;
    }

    // ---- actions --------------------------------------------------------------------------------

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift && _driveActions is not null)
        {
            switch (char.ToUpperInvariant(keyEvent.KeyChar))
            {
                case 'E':
                    Eject();
                    return true;
                case 'F':
                    await FormatAsync();
                    return true;
                case 'M':
                    await ShowImageDialogAsync(string.Empty);
                    return true;
                case 'B':
                    _shownRoot = string.Empty;
                    FollowCursor();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>Safely ejects the drive under the cursor on a background task.</summary>
    public Task EjectAsync() => Eject();

    private Task Eject()
    {
        if (_driveActions is null || SelectedDrive is not { DriveInfo: not null } drive)
        {
            SetStatus("Choose a drive to eject.", isError: true);
            return Task.CompletedTask;
        }

        if (_isBusy)
        {
            return PendingOperation;
        }

        var root = RootOf(drive);
        _isBusy = true;
        SetStatus($"Ejecting {root} ...", isError: false);
        PendingOperation = Task.Run(async () =>
        {
            try
            {
                var result = await _driveActions.EjectAsync(root);
                SetStatus(result.Message, !result.Success);
                _notifications?.Notify("Eject", result.Message, result.Success ? NotificationSeverity.Success : NotificationSeverity.Error);
            }
            finally
            {
                _isBusy = false;
                StateHasChanged();
            }
        });
        return PendingOperation;
    }

    /// <summary>Opens the format dialog for the drive under the cursor, unless it must not be formatted.</summary>
    public async Task FormatAsync()
    {
        if (_driveActions is null || _formatDialog is null || SelectedDrive is not { DriveInfo: not null } drive)
        {
            SetStatus("Choose a drive to format.", isError: true);
            return;
        }

        var root = RootOf(drive);
        var inUse = (_contentModelService?.ContentModels ?? [])
            .Select(m => m.Url)
            .Where(u => u is not null && u.ContainerType == ContainerType.FileSystem)
            .Select(u => u.Path);
        if (_driveActions.WhyCannotFormat(root, inUse) is { } reason)
        {
            SetStatus(reason, isError: true);
            return;
        }

        string label = string.Empty, fileSystem = "NTFS";
        try
        {
            if (drive.DriveInfo is { IsReady: true } info)
            {
                label = info.VolumeLabel;
                fileSystem = info.DriveFormat;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Formatting is what fixes an unreadable volume: the dialog opens with its defaults.
        }

        _formatDialog.Load(root, label, fileSystem);
        SetStatus(string.Empty, isError: false);
        await ShowDialogAsync(_formatDialog);
    }

    /// <summary>Opens the disk-image dialog, with <paramref name="imagePath"/> already in its box.</summary>
    public async Task ShowImageDialogAsync(string imagePath)
    {
        if (_imageDialog is null)
        {
            return;
        }

        _imageDialog.Load(imagePath);
        await ShowDialogAsync(_imageDialog);
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
        StateHasChanged();
    }

    private static string RootOf(Drive drive) =>
        drive.DriveInfo?.Name ?? drive.Name.Trim();

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        if (_driveActions is null)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var hints = _driveActions.CanFormat
            ? " E:eject  F:format  M:disk image  B:BitLocker  Esc:close "
            : " E:eject  M:disk image  B:BitLocker  Esc:close ";
        var footer = hints.FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}
