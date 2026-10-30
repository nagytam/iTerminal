using iConsole.Localization;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services.Drives;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Mounts an ISO, VHD or VHDX as a drive, or detaches one that was. Opened from the disk manager
/// (M) and, with the file already filled in, from a panel with the cursor on an image.
/// </summary>
public class DiskImageDialog : FormDialog
{
    private const int ErrorRows = 3;
    private const int SecondButtonWidth = 17;

    private readonly IDriveActionsService _driveActions;
    private readonly INotificationService _notifications;
    private readonly IContentModelService _contentModelService;
    private readonly TextBox _textBoxPath;
    private readonly Label _labelHint;
    private readonly Label[] _errorLabels;
    private readonly Button _mountButton;
    private readonly Button _unmountButton;
    private readonly Button _cancelButton;
    private bool _isRunning;

    public DiskImageDialog(
        IDriveActionsService driveActions,
        INotificationService notifications,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(DiskImageDialog), themeService)
    {
        _driveActions = driveActions;
        _notifications = notifications;
        _contentModelService = contentModelService;
        Title = iConsole.Localization.UiText.Get("Dialog.DiskImage", "Disk image");
        var mountCommand = new ImageCommand(mount: true);
        var unmountCommand = new ImageCommand(mount: false);
        AddCommand(mountCommand);

        _textBoxPath = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.Image", "Image") };
        _labelHint = new Label("An .iso, .vhd or .vhdx file.", themeService);
        _errorLabels = [.. Enumerable.Range(0, ErrorRows).Select(_ => new Label(string.Empty, themeService))];
        _mountButton = new Button(iConsole.Localization.UiText.Get("Button.Mount", "Mount"), mountCommand, themeService);
        _unmountButton = new Button(iConsole.Localization.UiText.Get("Button.Unmount", "Unmount"), unmountCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_textBoxPath);
        AddControl(_labelHint);
        foreach (var label in _errorLabels)
        {
            AddControl(label);
        }

        AddControl(_mountButton);
        AddControl(_unmountButton);
        AddControl(_cancelButton);
    }

    public string ImagePath
    {
        get => _textBoxPath.Text ?? string.Empty;
        set => _textBoxPath.Text = value ?? string.Empty;
    }

    public string Error => string.Join(" ", _errorLabels.Select(l => l.Text).Where(t => t.Length > 0));

    /// <summary>What the last action said, success or not — the toast's text, which a test cannot read.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    /// <summary>The action started last; it exists so a test can wait for it.</summary>
    public Task<bool> ActionTask { get; private set; } = Task.FromResult(false);

    /// <summary>Sets the path the dialog opens with; call before showing it.</summary>
    public void Load(string imagePath)
    {
        ImagePath = imagePath;
        LastMessage = string.Empty;
        _isRunning = false;
        SetError(string.Empty);
    }

    public override async Task OnInitializeAsync()
    {
        var length = ImagePath.Length;
        _textBoxPath.ScrollableModel.Reset();
        _textBoxPath.ScrollableModel.ScrollableSize = new Size(length, 1);
        _textBoxPath.ScrollableModel.CursorPosition.X = length;
        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent != null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 8, 50, 90), 11));
        }

        var row = 2;
        var controls = new List<IControl> { _textBoxPath, _labelHint };
        controls.AddRange(_errorLabels);
        foreach (var control in controls)
        {
            control.Position = new Position(FieldX, row++);
            control.ViewSize = new Size(FieldWidth, 1);
        }

        _textBoxPath.ScrollableModel.ViewSize = _textBoxPath.ViewSize;
        LayoutButtonRow(_mountButton, _cancelButton);
        _unmountButton.Position = new Position(FieldX + PrimaryButtonWidth + 2, _mountButton.Position.Y);
        _unmountButton.ViewSize = new Size(SecondButtonWidth, 1);
    }

    protected override void ClearError() => SetError(string.Empty);

    private void SetError(string message)
    {
        var lines = message.Length == 0 ? [] : ("Error: " + message).WrapWords((int)Math.Max(10, FieldWidth));
        for (var i = 0; i < _errorLabels.Length; i++)
        {
            _errorLabels[i].Text = i < lines.Count ? lines[i] : string.Empty;
        }

        StateHasChanged();
    }

    /// <summary>Mounts or unmounts the image in the box; the dialog closes on success and keeps the message on failure.</summary>
    public Task<bool> RunAsync(bool mount)
    {
        ActionTask = ExecuteAsync(mount);
        return ActionTask;
    }

    private async Task<bool> ExecuteAsync(bool mount)
    {
        if (_isRunning)
        {
            return false;
        }

        var path = ImagePath.Trim().Trim('"');
        if (path.StartsWith(Container.FILE, StringComparison.OrdinalIgnoreCase))
        {
            path = path[Container.FILE.Length..];
        }

        if (path.Length == 0)
        {
            SetError("Type the path of an .iso, .vhd or .vhdx file.");
            return false;
        }

        if (!DriveActionsService.IsImage(path))
        {
            SetError("Only .iso, .vhd and .vhdx files can be mounted.");
            return false;
        }

        if (mount && !File.Exists(path))
        {
            SetError($"{path} does not exist.");
            return false;
        }

        _isRunning = true;
        SetError(string.Empty);
        try
        {
            var result = await Task.Run(() => mount ? _driveActions.MountImageAsync(path) : _driveActions.UnmountImageAsync(path));
            LastMessage = result.Message;
            if (!result.Success)
            {
                SetError(result.Message);
                return false;
            }

            _notifications.Notify(mount ? "Mounted" : "Unmounted", result.Message, NotificationSeverity.Success);
            Close();
            RefreshPanels();
            return true;
        }
        finally
        {
            _isRunning = false;
        }
    }

    private void RefreshPanels()
    {
        foreach (var model in _contentModelService?.ContentModels.ToList() ?? [])
        {
            try
            {
                model.Refresh(forceRefresh: true);
            }
            catch
            {
                // A panel that cannot be re-read must not keep the other from it.
            }
        }
    }

    /// <summary>Enter (mount) or the Unmount button.</summary>
    private sealed class ImageCommand(bool mount) : Command
    {
        public override string Title => mount ? "Mount" : "Unmount";
        public override string Description => mount ? "Mount the image as a drive." : "Detach the image.";

        public override Key? Key { get; init; } =
            mount ? new Key(ConsoleKey.Enter, false, false, false) : null;

        public override async Task ExecuteAsync(object? args)
        {
            var dialog = Control as DiskImageDialog ?? Control?.Parent as DiskImageDialog;
            if (dialog is not null)
            {
                await dialog.RunAsync(mount);
            }
        }
    }
}
