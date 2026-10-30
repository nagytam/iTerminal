using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

public class DeleteDialog : FormDialog
{
    private readonly IContentModelService _contentModelService;

    public string Url
    {
        get
        {
            return _textBoxName?.Text ?? string.Empty;
        }
        set
        {
            if (_textBoxName.Text != value)
            {
                _textBoxName.Text = value;
            }
        }
    }

    /// <summary>
    /// Everything the delete acts on: the marked rows of the panel, or the one under the cursor.
    /// With more than one, <see cref="Url"/> only describes them.
    /// </summary>
    public IReadOnlyList<Url> Urls { get; set; } = [];

    /// <summary>True when several entries are deleted at once.</summary>
    public bool IsMultiple => Urls.Count > 1;

    /// <summary>The panel the entries were taken from, set by the command that opens the dialog.</summary>
    public IContentModel? SourceModel { get; set; }

    public bool MoveToRecycleBin => _checkBoxRecycleBin.IsChecked;
    public bool Recursive => _checkBoxRecursive.IsChecked;
    private Commands.DeleteCommand _deleteCommand { get; set; }
    private CancelDialogCommand _cancelCommand { get; set; }
    private TextBox _textBoxName { get; set; }
    private CheckBox _checkBoxRecycleBin { get; set; }
    private CheckBox _checkBoxRecursive { get; set; }
    private Button _deleteButton { get; set; }
    private Button _cancelButton { get; set; }
    public Url BasePath { get; internal set; } = null!;
    private Label _errorLabel { get; set; }
    public Label ErrorMessage => _errorLabel;

    protected override void ClearError() => _errorLabel.Text = string.Empty;

    public DeleteDialog(
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        TaskManager taskManager,
        ProgressWindowFactory progressWindowFactory,
        IErrorPromptService errorPromptService,
        IFileSystemService fileSystemService,
        IMemoryDriveService memoryDriveService,
        ISshFileSystemService sshFileSystemService,
        IArchiveFileSystemService archiveFileSystemService)
        : base(nameof(DeleteDialog), themeService)
    {
        _contentModelService = contentModelService;
        Title = iConsole.Localization.UiText.Get("Menu.Delete", "Delete");

        _deleteCommand = new Commands.DeleteCommand(
            contentModelService,
            taskManager,
            progressWindowFactory,
            errorPromptService,
            new FileSystemResolver(fileSystemService, memoryDriveService, sshFileSystemService, archiveFileSystemService, contentModelService));
        _cancelCommand = new CancelDialogCommand();
        AddCommand(_deleteCommand);
        AddCommand(_cancelCommand);

        _textBoxName = new TextBox(beepService, dateTimeProviderService, themeService) 
        { 
            Label = "Name", 
            Text = Url, 
            IsReadOnly = true 
        };
        _checkBoxRecycleBin = new CheckBox(themeService)
        {
            Label = "Move to Recycle bin",
            IsChecked = false,
            HotKeyChar = 'M'
        };
        _checkBoxRecursive = new CheckBox(themeService)
        {
            Label = "Recursively delete folder",
            IsChecked = false,
            IsEnabled = false,
            HotKeyChar = 'R'
        };
        _deleteButton = new Button("Delete", _deleteCommand, themeService) { HotKeyChar = 'D' };
        _cancelButton = new CancelDialogButton(themeService);
        _errorLabel = new Label(string.Empty, themeService);
        AddControl(_textBoxName);
        AddControl(_checkBoxRecycleBin);
        AddControl(_checkBoxRecursive);
        AddControl(_errorLabel);
        AddControl(_deleteButton);
        AddControl(_cancelButton);
    }

    public override async Task OnInitializeAsync()
    {
        var sourceModel = SourceModel ?? _contentModelService.GetFocusedModel() ?? _contentModelService.GetLastContentModel();
        var urls = iTerminal.Core.Commands.FocusedEntry.ActionableUrls(sourceModel);
        Urls = urls;
        var url = urls.Count > 0 ? urls[0] : _contentModelService.GetFocusedModel()?.GetFocusedEntry()?.Url;
        if (IsMultiple)
        {
            // The box is read-only and only says what is going; Urls is what is deleted.
            Url = TransferDialog.DescribeSources(urls);
            _textBoxName.ScrollableModel.Reset();
            _textBoxName.ScrollableModel.ScrollableSize = new Size(Url.Length, 1);
            var anyFolder = urls.Any(u => u.PathType == PathType.Folder);
            _checkBoxRecursive.IsEnabled = anyFolder;
            _checkBoxRecursive.IsChecked = anyFolder;
            ApplyRecycleBinAvailability(urls[0]);
        }
        else if (url is not null)
        {
            Url = url.FullUrl ?? string.Empty;
            _textBoxName.ScrollableModel.Reset();
            _textBoxName.ScrollableModel.ScrollableSize = new Size(Url.Length, 1);
            _checkBoxRecursive.IsEnabled = url.PathType == PathType.Folder;
            _checkBoxRecursive.IsChecked = url.PathType == PathType.Folder;
            ApplyRecycleBinAvailability(url);
        }

        if (IsMultiple)
        {
            Title = $"Delete {Urls.Count} items";
        }
        else if (new Url(Url).PathType == PathType.File)
        {
            Title = "Delete file";
        }
        else
        {
            Title = "Delete folder";
        }


        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(80, 8));
        }

        _textBoxName.Position = new Position(FieldX, 2);
        _textBoxName.ViewSize = new Size(FieldWidth, 1);
        _textBoxName.ScrollableModel.ViewSize = _textBoxName.ViewSize;
        _checkBoxRecycleBin.ViewSize = new Size(FieldWidth, 1);
        _checkBoxRecursive.ViewSize = new Size(FieldWidth, 1);
        _errorLabel.ViewSize = new Size(FieldWidth, 1);
        LayoutButtonRow(_deleteButton, _cancelButton);

        Layout.Bottom(_checkBoxRecycleBin, _textBoxName);
        Layout.Bottom(_checkBoxRecursive, _checkBoxRecycleBin);
        Layout.Bottom(_errorLabel, _checkBoxRecursive);

        ShowEndOfPath();
    }

    /// <summary>
    /// Scrolls the read-only path to its end. Shown from the start, a long path left the one part
    /// that matters — the name of what is about to be deleted — past the right edge of the box.
    /// </summary>
    private void ShowEndOfPath()
    {
        var length = Url.Length;
        var visible = Math.Max(1, _textBoxName.EditableWidth);
        _textBoxName.ScrollableModel.CursorPosition.X = length;
        _textBoxName.ScrollableModel.ScrollPosition.X = Math.Max(0, length - visible + 1);
    }

    internal const string RecycleBinLabel = "Move to Recycle bin";
    internal const string NoRecycleBinLabel = "Move to Recycle bin — not available here, deleted for good";
    internal const string TrashLabel = "Move to Trash";

    /// <summary>Whether a delete of <paramref name="url"/> can be undone: local files on Windows (Recycle bin) and Linux (Trash).</summary>
    internal static bool CanRecycle(Url url) =>
        (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && url.ContainerType == ContainerType.FileSystem;

    /// <summary>
    /// Recoverable by default where that exists, and honest where it does not. The box used to start
    /// unchecked everywhere — a permanent delete one Enter away — and stayed tickable on drives that
    /// ignore it (SSH, SMB, the memory drive), where ticking it promised a way back that was not there.
    /// </summary>
    private void ApplyRecycleBinAvailability(Url url)
    {
        var canRecycle = CanRecycle(url);
        _checkBoxRecycleBin.IsEnabled = canRecycle;
        _checkBoxRecycleBin.IsChecked = canRecycle;
        _checkBoxRecycleBin.Label = !canRecycle ? NoRecycleBinLabel : OperatingSystem.IsLinux() ? TrashLabel : RecycleBinLabel;
    }

    public void Reset()
    {
        BasePath = string.Empty;
        Url = string.Empty;
        Urls = [];
        SourceModel = null;
        _errorLabel.Text = string.Empty;
    }
}
