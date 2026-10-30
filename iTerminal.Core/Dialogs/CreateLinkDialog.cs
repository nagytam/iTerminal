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
/// Ctrl+L: makes a link to the file or folder under the cursor — symbolic by default, a hard link
/// when the box is ticked (files only). The link is offered in the other panel's folder under the
/// same name, the way F5 offers its copy, and the path can be changed to anything.
/// </summary>
public class CreateLinkDialog : FormDialog
{
    private const int ErrorRows = 3;

    private readonly ILinkService _linkService;
    private readonly IContentModelService _contentModelService;
    private readonly Label _labelTarget;
    private readonly TextBox _textBoxLink;
    private readonly CheckBox _checkBoxHard;
    private readonly CheckBox _checkBoxRelative;
    private readonly Label[] _errorLabels;
    private readonly Button _createButton;
    private readonly Button _cancelButton;

    public CreateLinkDialog(
        ILinkService linkService,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(CreateLinkDialog), themeService)
    {
        _linkService = linkService;
        _contentModelService = contentModelService;
        Title = iConsole.Localization.UiText.Get("Dialog.CreateLink", "Create link");
        var createCommand = new CreateCommand();
        AddCommand(createCommand);

        _textBoxLink = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Link" };
        _labelTarget = new Label(string.Empty, themeService);
        _checkBoxHard = new CheckBox(themeService) { Label = "Hard link (a second name for the file; same volume)" };
        _checkBoxRelative = new CheckBox(themeService) { Label = "Relative target (the pair can be moved together)" };
        _errorLabels = [.. Enumerable.Range(0, ErrorRows).Select(_ => new Label(string.Empty, themeService))];
        _createButton = new Button("Create", createCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        // The box first: it is where the cursor belongs when the dialog opens.
        AddControl(_textBoxLink);
        AddControl(_labelTarget);
        AddControl(_checkBoxHard);
        AddControl(_checkBoxRelative);
        foreach (var label in _errorLabels)
        {
            AddControl(label);
        }
        AddControl(_createButton);
        AddControl(_cancelButton);
    }

    /// <summary>The existing file or folder the link will lead to.</summary>
    public string TargetPath { get; private set; } = string.Empty;

    public bool TargetIsFolder { get; private set; }

    public string LinkPath
    {
        get => _textBoxLink.Text ?? string.Empty;
        set => _textBoxLink.Text = value ?? string.Empty;
    }

    public bool IsHardLink
    {
        get => _checkBoxHard.IsChecked;
        set => _checkBoxHard.IsChecked = value && !TargetIsFolder;
    }

    public bool IsRelative
    {
        get => _checkBoxRelative.IsChecked;
        set => _checkBoxRelative.IsChecked = value;
    }

    public string Error => string.Join(" ", _errorLabels.Select(l => l.Text).Where(t => t.Length > 0));

    /// <summary>The creation started last; it exists so a test can wait for it.</summary>
    public Task CreateTask { get; private set; } = Task.CompletedTask;

    /// <summary>Sets what the dialog is about; call before showing it.</summary>
    public void Load(string targetPath, bool targetIsFolder, string linkPath)
    {
        TargetPath = targetPath;
        TargetIsFolder = targetIsFolder;
        LinkPath = linkPath;
        _checkBoxHard.IsChecked = false;
        // A folder cannot be hard-linked; the box says so by being unavailable rather than by an
        // error after Enter.
        _checkBoxHard.IsEnabled = !targetIsFolder;
        _checkBoxRelative.IsChecked = false;
        SetError(string.Empty);
    }

    /// <summary>
    /// Where a link to <paramref name="targetPath"/> is offered: under the same name in
    /// <paramref name="otherFolder"/> when that is another local folder, otherwise beside the
    /// target as "name - link.ext" — the same name in the same folder is the target itself.
    /// </summary>
    public static string DefaultLinkPath(string targetPath, string? otherFolder)
    {
        var target = Path.TrimEndingDirectorySeparator(targetPath);
        var folder = Path.GetDirectoryName(target) ?? string.Empty;
        var name = Path.GetFileName(target);
        if (!string.IsNullOrEmpty(otherFolder))
        {
            var other = Path.TrimEndingDirectorySeparator(otherFolder);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(other, folder, comparison))
            {
                return Path.Combine(other, name);
            }
        }

        return Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} - link{Path.GetExtension(name)}");
    }

    public override async Task OnInitializeAsync()
    {
        _labelTarget.Text = "to   " + TargetPath;
        var length = LinkPath.Length;
        _textBoxLink.ScrollableModel.Reset();
        _textBoxLink.ScrollableModel.ScrollableSize = new Size(length, 1);
        _textBoxLink.ScrollableModel.CursorPosition.X = length;

        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 8, 50, 90), 12));
        }

        var row = 2;
        var controls = new List<IControl> { _textBoxLink, _labelTarget, _checkBoxHard, _checkBoxRelative };
        controls.AddRange(_errorLabels);
        foreach (var control in controls)
        {
            control.Position = new Position(FieldX, row++);
            control.ViewSize = new Size(FieldWidth, 1);
        }

        _textBoxLink.ScrollableModel.ViewSize = _textBoxLink.ViewSize;
        _labelTarget.Text = ("to   " + TargetPath).Shrink((int)FieldWidth) ?? string.Empty;
        LayoutButtonRow(_createButton, _cancelButton);
    }

    protected override void ClearError() => SetError(string.Empty);

    /// <summary>A failure's message is a sentence or two: wrapped over the rows kept for it, not cut at the border.</summary>
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
    /// Creates the link and closes the dialog; a failure stays in the dialog, in words. Returns
    /// whether the link was made.
    /// </summary>
    public async Task<bool> CreateAsync()
    {
        var linkPath = LinkPath.Trim();
        if (linkPath.Length == 0)
        {
            SetError("Type where the link goes.");
            return false;
        }

        if (linkPath.StartsWith(Container.FILE, StringComparison.OrdinalIgnoreCase))
        {
            linkPath = linkPath[Container.FILE.Length..];
        }

        try
        {
            var kind = IsHardLink ? LinkKind.Hard : LinkKind.Symbolic;
            var create = _linkService.CreateAsync(kind, linkPath, TargetPath, IsRelative);
            CreateTask = create;
            await create;
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

    /// <summary>
    /// The local drive's watcher would notice the new entry by itself in a moment; this is for the
    /// moment — and for a hard link, which changes nothing in the folder of the file it names.
    /// </summary>
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

    /// <summary>Enter, or the Create button.</summary>
    private sealed class CreateCommand : Command
    {
        public override string Title => "Create";
        public override string Description => "Create the link.";

        public override Key? Key { get; init; } =
            new Key(ConsoleKey.Enter, false, false, false);

        public override async Task ExecuteAsync(object? args)
        {
            var dialog = Control as CreateLinkDialog ?? Control?.Parent as CreateLinkDialog;
            if (dialog is not null)
            {
                await dialog.CreateAsync();
            }
        }
    }
}
