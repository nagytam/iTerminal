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
/// Formats a drive. Opened from the disk manager (F on a drive), never by a key of its own — and made
/// hard to do by accident: the drive letter has to be typed back before Format does anything, and a
/// drive the system, a panel or this very program is using is refused before the dialog opens
/// (<see cref="IDriveActionsService.WhyCannotFormat"/>).
/// </summary>
public class FormatDriveDialog : FormDialog
{
    private const int ErrorRows = 3;

    private readonly IDriveActionsService _driveActions;
    private readonly INotificationService _notifications;
    private readonly IContentModelService _contentModelService;
    private readonly Label _labelDrive;
    private readonly Label _labelWarning;
    private readonly ComboBox<string> _comboFileSystem;
    private readonly TextBox _textBoxLabel;
    private readonly CheckBox _checkBoxQuick;
    private readonly TextBox _textBoxConfirm;
    private readonly Label[] _errorLabels;
    private readonly Button _formatButton;
    private readonly Button _cancelButton;
    private bool _isFormatting;

    public FormatDriveDialog(
        IDriveActionsService driveActions,
        INotificationService notifications,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(FormatDriveDialog), themeService)
    {
        _driveActions = driveActions;
        _notifications = notifications;
        _contentModelService = contentModelService;
        Title = iConsole.Localization.UiText.Get("Dialog.FormatDrive", "Format drive");
        var formatCommand = new FormatCommand();
        AddCommand(formatCommand);

        _labelDrive = new Label(string.Empty, themeService);
        _labelWarning = new Label(string.Empty, themeService);
        _comboFileSystem = new ComboBox<string>(themeService) { Label = "File system" };
        _comboFileSystem.SetItems(DriveActionsService.FileSystems);
        _textBoxLabel = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Volume label" };
        _checkBoxQuick = new CheckBox(themeService) { Label = "Quick format (skip the full surface pass)", IsChecked = true };
        _textBoxConfirm = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Type the drive letter to confirm" };
        _errorLabels = [.. Enumerable.Range(0, ErrorRows).Select(_ => new Label(string.Empty, themeService))];
        _formatButton = new Button("Format", formatCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_comboFileSystem);
        AddControl(_textBoxLabel);
        AddControl(_checkBoxQuick);
        AddControl(_textBoxConfirm);
        AddControl(_labelDrive);
        AddControl(_labelWarning);
        foreach (var label in _errorLabels)
        {
            AddControl(label);
        }

        AddControl(_formatButton);
        AddControl(_cancelButton);
    }

    /// <summary>The drive being formatted, as the disk manager lists it (<c>E:\</c>).</summary>
    public string Root { get; private set; } = string.Empty;

    public string FileSystem
    {
        get => _comboFileSystem.SelectedItem ?? DriveActionsService.FileSystems[0];
        set
        {
            var index = DriveActionsService.FileSystems.ToList()
                .FindIndex(f => string.Equals(f, value, StringComparison.OrdinalIgnoreCase));
            _comboFileSystem.SelectedIndex = Math.Max(0, index);
        }
    }

    public string VolumeLabel
    {
        get => _textBoxLabel.Text ?? string.Empty;
        set => _textBoxLabel.Text = value ?? string.Empty;
    }

    public bool Quick
    {
        get => _checkBoxQuick.IsChecked;
        set => _checkBoxQuick.IsChecked = value;
    }

    public string Confirmation
    {
        get => _textBoxConfirm.Text ?? string.Empty;
        set => _textBoxConfirm.Text = value ?? string.Empty;
    }

    public string Error => string.Join(" ", _errorLabels.Select(l => l.Text).Where(t => t.Length > 0));

    /// <summary>The format started last; it exists so a test can wait for it.</summary>
    public Task<bool> FormatTask { get; private set; } = Task.FromResult(false);

    /// <summary>Sets what the dialog is about; call before showing it.</summary>
    public void Load(string root, string currentLabel, string currentFileSystem)
    {
        Root = root;
        VolumeLabel = currentLabel;
        FileSystem = DriveActionsService.FileSystems.Any(f => string.Equals(f, currentFileSystem, StringComparison.OrdinalIgnoreCase))
            ? currentFileSystem
            : "NTFS";
        Quick = true;
        Confirmation = string.Empty;
        _isFormatting = false;
        SetError(string.Empty);
        _labelDrive.Text = $"Drive {root}";
        _labelWarning.Text = "Everything on this drive will be erased.";
    }

    public override async Task OnInitializeAsync()
    {
        _textBoxLabel.ScrollableModel.Reset();
        _textBoxLabel.ScrollableModel.ScrollableSize = new Size(VolumeLabel.Length, 1);
        _textBoxConfirm.ScrollableModel.Reset();
        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent != null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 8, 50, 80), 16));
        }

        var row = 2;
        var controls = new List<IControl> { _labelDrive, _labelWarning, _comboFileSystem, _textBoxLabel, _checkBoxQuick, _textBoxConfirm };
        controls.AddRange(_errorLabels);
        foreach (var control in controls)
        {
            control.Position = new Position(FieldX, row++);
            control.ViewSize = new Size(FieldWidth, 1);
        }

        _textBoxLabel.ScrollableModel.ViewSize = _textBoxLabel.ViewSize;
        _textBoxConfirm.ScrollableModel.ViewSize = _textBoxConfirm.ViewSize;
        LayoutButtonRow(_formatButton, _cancelButton);
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

    /// <summary>
    /// Checks the confirmation, formats on a background task and closes the dialog when it worked; a
    /// failure stays in the dialog, in words. Returns whether the drive was formatted.
    /// </summary>
    public Task<bool> FormatAsync()
    {
        FormatTask = RunAsync();
        return FormatTask;
    }

    private async Task<bool> RunAsync()
    {
        if (_isFormatting)
        {
            // Enter again while the drive is being formatted: say so, rather than doing nothing that looks like a failure.
            SetError("The drive is already being formatted.");
            return false;
        }

        if (!DriveActionsService.TryLetter(Root, out var letter))
        {
            SetError("Not a lettered drive.");
            return false;
        }

        var typed = Confirmation.Trim().TrimEnd(':', '\\', '/');
        if (!string.Equals(typed, letter.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            SetError($"Type {letter} to confirm that {letter}: is to be erased.");
            return false;
        }

        _isFormatting = true;
        SetError(string.Empty);
        _labelWarning.Text = $"Formatting {letter}: ...";
        StateHasChanged();
        try
        {
            var result = await Task.Run(() => _driveActions.FormatAsync(Root, new DriveFormatOptions(FileSystem, VolumeLabel.Trim(), Quick)));
            if (!result.Success)
            {
                _labelWarning.Text = "Everything on this drive will be erased.";
                SetError(result.Message);
                return false;
            }

            _notifications.Notify("Format", result.Message, NotificationSeverity.Success);
            Close();
            RefreshPanels();
            return true;
        }
        finally
        {
            _isFormatting = false;
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

    /// <summary>Enter, or the Format button.</summary>
    private sealed class FormatCommand : Command
    {
        public override string Title => "Format";
        public override string Description => "Format the drive.";

        public override Key? Key { get; init; } =
            new Key(ConsoleKey.Enter, false, false, false);

        public override async Task ExecuteAsync(object? args)
        {
            var dialog = Control as FormatDriveDialog ?? Control?.Parent as FormatDriveDialog;
            if (dialog is not null)
            {
                await dialog.FormatAsync();
            }
        }
    }
}
