using iConsole.Localization;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services.Links;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+Shift+L: changes where an existing link leads. A symbolic link gets a new target (written
/// relative or absolute) and can become a hard link; a file that has other names — a hard link —
/// can have this name pointed at another file, or turned into a symbolic link. A file with only one
/// name is not offered: pointing it elsewhere would lose its data.
/// </summary>
public class EditLinkDialog : FormDialog
{
    private const int ErrorRows = 3;

    private readonly ILinkService _linkService;
    private readonly IContentModelService _contentModelService;
    private readonly Label _labelLink;
    private readonly Label _labelKind;
    private readonly TextBox _textBoxTarget;
    private readonly CheckBox _checkBoxHard;
    private readonly CheckBox _checkBoxRelative;
    private readonly Label[] _errorLabels;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;

    public EditLinkDialog(
        ILinkService linkService,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(EditLinkDialog), themeService)
    {
        _linkService = linkService;
        _contentModelService = contentModelService;
        Title = iConsole.Localization.UiText.Get("Dialog.EditLink", "Edit link");
        var saveCommand = new SaveCommand();
        AddCommand(saveCommand);

        _textBoxTarget = new TextBox(beepService, dateTimeProviderService, themeService) { Label = iConsole.Localization.UiText.Get("Label.Target", "Target") };
        _labelLink = new Label(string.Empty, themeService);
        _labelKind = new Label(string.Empty, themeService);
        _checkBoxHard = new CheckBox(themeService) { Label = UiText.Get("Field.HardLink", "Hard link (a second name for the file; same volume)") };
        _checkBoxRelative = new CheckBox(themeService) { Label = UiText.Get("Field.RelativeTarget", "Relative target (the pair can be moved together)") };
        _errorLabels = [.. Enumerable.Range(0, ErrorRows).Select(_ => new Label(string.Empty, themeService))];
        _saveButton = new Button(iConsole.Localization.UiText.Get("Common.Save", "Save"), saveCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_textBoxTarget);
        AddControl(_labelLink);
        AddControl(_labelKind);
        AddControl(_checkBoxHard);
        AddControl(_checkBoxRelative);
        foreach (var label in _errorLabels)
        {
            AddControl(label);
        }
        AddControl(_saveButton);
        AddControl(_cancelButton);
    }

    /// <summary>The link being edited.</summary>
    public string LinkPath { get; private set; } = string.Empty;

    /// <summary>What the disk said about <see cref="LinkPath"/> when the dialog opened; null until it has.</summary>
    public LinkInfo? Info { get; private set; }

    public string TargetPath
    {
        get => _textBoxTarget.Text ?? string.Empty;
        set => _textBoxTarget.Text = value ?? string.Empty;
    }

    public bool IsHardLink
    {
        get => _checkBoxHard.IsChecked;
        set => _checkBoxHard.IsChecked = value;
    }

    public bool IsRelative
    {
        get => _checkBoxRelative.IsChecked;
        set => _checkBoxRelative.IsChecked = value;
    }

    public string Error => string.Join(" ", _errorLabels.Select(l => l.Text).Where(t => t.Length > 0));

    /// <summary>The inspection started on opening; it exists so a test can wait for it.</summary>
    public Task LoadTask { get; private set; } = Task.CompletedTask;

    /// <summary>The save started last; it exists so a test can wait for it.</summary>
    public Task SaveTask { get; private set; } = Task.CompletedTask;

    /// <summary>Sets which link the dialog is about; call before showing it.</summary>
    public void Load(string linkPath)
    {
        LinkPath = Path.TrimEndingDirectorySeparator(linkPath);
        Info = null;
        TargetPath = string.Empty;
        _checkBoxHard.IsChecked = false;
        _checkBoxRelative.IsChecked = false;
        _labelKind.Text = UiText.Get("Msg.ReadingLink", "Reading the link…");
        _saveButton.IsEnabled = false;
        SetError(string.Empty);
    }

    public override async Task OnInitializeAsync()
    {
        _labelLink.Text = "link " + LinkPath;
        LoadTask = InspectAsync();
        await LoadTask;
        await base.OnInitializeAsync();
    }

    private async Task InspectAsync()
    {
        LinkInfo info;
        try
        {
            info = await _linkService.InspectAsync(LinkPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _labelKind.Text = string.Empty;
            SetError(exception.Message);
            return;
        }

        Info = info;
        if (info.IsSymbolic)
        {
            var written = info.SymbolicTarget!;
            TargetPath = written;
            _checkBoxRelative.IsChecked = !Path.IsPathRooted(written);
            _labelKind.Text = (info.IsFolder ? UiText.Get("Msg.SymlinkFolder", "A symbolic link to a folder.") : UiText.Get("Msg.SymlinkFile", "A symbolic link to a file."));
            _saveButton.IsEnabled = true;
        }
        else if (info.IsHard)
        {
            _checkBoxHard.IsChecked = true;
            _labelKind.Text = string.Format(UiText.Get("Msg.HardLinkNames", "A hard link: this file has {0} names. Type the file this name should lead to."), info.NameCount);
            _saveButton.IsEnabled = true;
        }
        else
        {
            _labelKind.Text = string.Empty;
            SetError(info.IsFolder
                ? "A folder is not a link."
                : "This is not a link, and the file has no other name; pointing it elsewhere would lose it.");
        }

        var length = TargetPath.Length;
        _textBoxTarget.ScrollableModel.Reset();
        _textBoxTarget.ScrollableModel.ScrollableSize = new Size(length, 1);
        _textBoxTarget.ScrollableModel.CursorPosition.X = length;
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 8, 50, 90), 13));
        }

        var row = 2;
        var controls = new List<IControl> { _textBoxTarget, _labelLink, _labelKind, _checkBoxHard, _checkBoxRelative };
        controls.AddRange(_errorLabels);
        foreach (var control in controls)
        {
            control.Position = new Position(FieldX, row++);
            control.ViewSize = new Size(FieldWidth, 1);
        }

        _textBoxTarget.ScrollableModel.ViewSize = _textBoxTarget.ViewSize;
        _labelLink.Text = ("link " + LinkPath).Shrink((int)FieldWidth) ?? string.Empty;
        LayoutButtonRow(_saveButton, _cancelButton);
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
    /// Points the link at the typed target and closes the dialog; a failure stays in the dialog,
    /// in words, and the link is as it was. Returns whether the link was changed.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (Info is not { IsEditable: true })
        {
            return false;
        }

        var target = TargetPath.Trim();
        if (target.Length == 0)
        {
            SetError("Type where the link should lead.");
            return false;
        }

        if (target.StartsWith(Container.FILE, StringComparison.OrdinalIgnoreCase))
        {
            target = target[Container.FILE.Length..];
        }

        try
        {
            // A relative target is relative to the link's folder, as the system reads it.
            var folder = Path.GetDirectoryName(LinkPath) ?? string.Empty;
            var resolved = Path.GetFullPath(target, folder);
            var kind = IsHardLink ? LinkKind.Hard : LinkKind.Symbolic;
            var save = _linkService.ReplaceAsync(kind, LinkPath, resolved, IsRelative);
            SaveTask = save;
            await save;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetError(exception.Message);
            return false;
        }

        Close();
        RefreshPanels();
        return true;
    }

    /// <summary>A link that was re-pointed is listed with its new target at once, not when the watcher gets to it.</summary>
    private void RefreshPanels()
    {
        foreach (var model in _contentModelService?.ContentModels.ToList() ?? [])
        {
            try
            {
                if (model.Url?.ContainerType == ContainerType.FileSystem)
                {
                    model.Refresh(forceRefresh: true);
                }
            }
            catch
            {
                // A panel that cannot be re-read must not keep the other from it.
            }
        }
    }

    /// <summary>Enter, or the Save button.</summary>
    private sealed class SaveCommand : Command
    {
        public override string Title => "Save";
        public override string Description => "Save the changed link.";

        public override Key? Key { get; init; } =
            new Key(ConsoleKey.Enter, false, false, false);

        public override async Task ExecuteAsync(object? args)
        {
            var dialog = Control as EditLinkDialog ?? Control?.Parent as EditLinkDialog;
            if (dialog is not null)
            {
                await dialog.SaveAsync();
            }
        }
    }
}
