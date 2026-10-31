using iConsole.Localization;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Factories;
using iTerminal.Core.Helper;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Shift+F5 — Alt+F5's other half: unpacks the marked archives, or the one under the cursor, into a
/// folder (by default the other panel's).
///
/// <para>"Into a folder of its own" is on by default: an archive that scatters forty files into the
/// folder the user was looking at is the classic mess, and one folder per archive is also what
/// keeps several archives that contain a file of the same name from overwriting each other. The
/// line under the folder box says where the files will really land.</para>
/// </summary>
public class ExtractDialog : FormDialog
{
    private readonly IContentModelService _contentModelService;
    private readonly IArchiveExtractionService _extractionService;
    private readonly Label _labelSource;
    private readonly FormPanel _form;
    private readonly TextBox _textBoxTarget;
    private readonly PasswordBox _password;
    private readonly CheckBox _checkBoxOwnFolder;
    private readonly CheckBox _checkBoxOverwrite;
    private readonly Label _labelLanding;
    private readonly Label _errorLabel;
    private readonly Button _extractButton;
    private readonly Button _cancelButton;

    /// <summary>Rows: border, blank, source, blank, two form rows, two check boxes, landing line, error, blank, buttons, border.</summary>
    internal const int DialogHeight = 14;

    /// <summary>The panel the archives come from, set by the command that opens the dialog.</summary>
    public IContentModel? SourceModel { get; set; }

    /// <summary>The archives to unpack: the marked rows of <see cref="SourceModel"/>, or the cursor row — archives only.</summary>
    public IReadOnlyList<Url> Archives { get; private set; } = [];

    public string TargetFolder
    {
        get => _textBoxTarget.Text ?? string.Empty;
        set => _textBoxTarget.Text = value ?? string.Empty;
    }

    public string Password
    {
        get => _password.Text ?? string.Empty;
        set => _password.Text = value ?? string.Empty;
    }

    public bool EachIntoOwnFolder
    {
        get => _checkBoxOwnFolder.IsChecked;
        set => _checkBoxOwnFolder.IsChecked = value;
    }

    public bool Overwrite
    {
        get => _checkBoxOverwrite.IsChecked;
        set => _checkBoxOverwrite.IsChecked = value;
    }

    public Label ErrorMessage => _errorLabel;

    /// <summary>What decides which rows are archives; the command opening the dialog asks it too.</summary>
    internal IArchiveExtractionService ExtractionService => _extractionService;

    internal string SourceLabelForTest => _labelSource.Text;
    internal string LandingForTest => _labelLanding.Text;

    public ExtractDialog(
        IContentModelService contentModelService,
        IArchiveExtractionService extractionService,
        TaskManager taskManager,
        ProgressWindowFactory progressWindowFactory,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        IFileSystemService fileSystemService,
        IMemoryDriveService memoryDriveService,
        ISshFileSystemService sshFileSystemService,
        IArchiveFileSystemService archiveFileSystemService)
        : base(nameof(ExtractDialog), themeService)
    {
        _contentModelService = contentModelService;
        _extractionService = extractionService;
        Title = iConsole.Localization.UiText.Get("Menu.Extract", "Extract");

        var extractCommand = new ExtractCommand(
            extractionService, taskManager, progressWindowFactory, contentModelService,
            new FileSystemResolver(fileSystemService, memoryDriveService, sshFileSystemService, archiveFileSystemService, contentModelService));
        AddCommand(extractCommand);

        _labelSource = new Label(iConsole.Localization.UiText.Get("Label.Source", "Source") + ": ", themeService);
        _textBoxTarget = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            MaxLength = Url.MaxLength,
            IsReadOnly = false,
        };
        _textBoxTarget.PropertyChanged += (_, _) => UpdateLanding();
        _password = new PasswordBox(beepService, dateTimeProviderService, themeService);

        _form = new FormPanel(themeService);
        _form.AddField("To", _textBoxTarget);
        _form.AddField("Password", _password);

        _checkBoxOwnFolder = new CheckBox(themeService)
        {
            Label = UiText.Get("Field.EachArchiveOwnFolder", "Each archive into a folder of its own"),
            IsChecked = true,
            HotKeyChar = iConsole.Localization.HotKeys.VisibleIn(iConsole.Localization.UiText.Get("Field.EachArchiveOwnFolder", "Each archive into a folder of its own"), 'F'),
        };
        _checkBoxOwnFolder.CheckedChanged += (_, _) => UpdateLanding();
        _checkBoxOverwrite = new CheckBox(themeService)
        {
            Label = UiText.Get("Field.OverwriteExisting", "Overwrite files that are already there"),
            IsChecked = false,
            HotKeyChar = iConsole.Localization.HotKeys.VisibleIn(iConsole.Localization.UiText.Get("Field.OverwriteExisting", "Overwrite files that are already there"), 'O'),
        };
        _labelLanding = new Label(string.Empty, themeService);
        _errorLabel = new Label(string.Empty, themeService);
        _extractButton = new Button(iConsole.Localization.UiText.Get("Menu.Extract", "Extract"), extractCommand, themeService) { HotKeyChar = iConsole.Localization.HotKeys.VisibleIn(iConsole.Localization.UiText.Get("Menu.Extract", "Extract"), 'E') };
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_labelSource);
        AddControl(_form);
        AddControl(_checkBoxOwnFolder);
        AddControl(_checkBoxOverwrite);
        AddControl(_labelLanding);
        AddControl(_errorLabel);
        AddControl(_extractButton);
        AddControl(_cancelButton);
    }

    /// <summary>The archives among <paramref name="urls"/>: files whose name an extractor knows.</summary>
    internal static IReadOnlyList<Url> ArchivesAmong(IReadOnlyList<Url> urls, IArchiveExtractionService extractionService) =>
        [.. urls.Where(u => u.PathType != PathType.Folder && extractionService.IsArchive(FocusedEntry.NameOf(u)))];

    public override async Task OnInitializeAsync()
    {
        var sourceModel = SourceModel ?? _contentModelService.GetFocusedModel() ?? _contentModelService.GetLastContentModel();
        Archives = ArchivesAmong(FocusedEntry.ActionableUrls(sourceModel), _extractionService);
        Password = string.Empty;
        _errorLabel.Text = string.Empty;

        Title = Archives.Count == 1 ? $"Extract {FocusedEntry.NameOf(Archives[0])}" : $"Extract {Archives.Count} archives";
        _labelSource.Text = iConsole.Localization.UiText.Get("Label.Source", "Source") + ": " + string.Join(", ", Archives.Select(FocusedEntry.NameOf));
        var target = _contentModelService.NonFocusedFolderUrl ?? sourceModel?.Url;
        TargetFolder = target?.FullUrl ?? string.Empty;

        // Said up front, before Enter: an archive this machine cannot open is better named now than
        // listed as a failure after the others have been unpacked.
        var blocked = Archives.Select(u => _extractionService.WhyCannotExtract(FocusedEntry.NameOf(u))).FirstOrDefault(r => r is not null);
        _blockedNotice = blocked ?? string.Empty;
        _errorLabel.Text = _blockedNotice;

        UpdateLanding();
        await base.OnInitializeAsync();
    }

    /// <summary>Where the files land: the folder in the box, or a folder per archive inside it.</summary>
    private void UpdateLanding()
    {
        if (_labelLanding is null || _textBoxTarget is null || _checkBoxOwnFolder is null)
        {
            return;
        }

        string text;
        if (Archives.Count == 0 || string.IsNullOrWhiteSpace(TargetFolder))
        {
            text = string.Empty;
        }
        else if (!EachIntoOwnFolder)
        {
            text = "Into: " + TargetFolder;
        }
        else if (Archives.Count == 1)
        {
            text = "Into: " + DestinationFor(Archives[0]).FullUrl;
        }
        else
        {
            text = "Into: a folder per archive in " + TargetFolder;
        }

        var shrunk = text.Shrink((int)FieldWidth) ?? text;
        if (_labelLanding.Text != shrunk)
        {
            _labelLanding.Text = shrunk;
            StateHasChanged();
        }
    }

    internal Url DestinationFor(Url archive) =>
        ExtractTask.DestinationFor(new Url(TargetFolder), _extractionService.FolderNameFor(FocusedEntry.NameOf(archive)), EachIntoOwnFolder);

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(new Size(76, DialogHeight));
        }

        var width = new Size(FieldWidth, 1);
        _labelSource.Text = _labelSource.Text.Shrink((int)FieldWidth) ?? string.Empty;
        _labelSource.Position = new Position(FieldX, 2);
        _labelSource.ViewSize = width;
        _form.Position = new Position(FieldX, 4);
        _form.ViewSize = new Size(FieldWidth, 2);
        _checkBoxOwnFolder.Position = new Position(FieldX, 6);
        _checkBoxOwnFolder.ViewSize = width;
        _checkBoxOverwrite.Position = new Position(FieldX, 7);
        _checkBoxOverwrite.ViewSize = width;
        _labelLanding.Position = new Position(FieldX, 8);
        _labelLanding.ViewSize = width;
        _errorLabel.Position = new Position(FieldX, 9);
        _errorLabel.ViewSize = width;
        LayoutButtonRow(_extractButton, _cancelButton);

        base.OnInitializeLayout();
        UpdateLanding();
    }

    /// <summary>The settings, or why they cannot be used.</summary>
    internal (ExtractionOptions? Options, string? Error) ReadOptions()
    {
        if (Archives.Count == 0)
        {
            return (null, "Nothing to extract: mark or choose an archive.");
        }

        if (string.IsNullOrWhiteSpace(TargetFolder))
        {
            return (null, "Type the folder to extract into.");
        }

        return (new ExtractionOptions(Password.Length > 0 ? Password : null, Overwrite), null);
    }

    /// <summary>What was said up front about an archive this machine cannot open; typing on withdraws an error, not this.</summary>
    private string _blockedNotice = string.Empty;

    protected override void ClearError() => _errorLabel.Text = _blockedNotice;

    public void Reset()
    {
        SourceModel = null;
        Archives = [];
        Password = string.Empty;
        _blockedNotice = string.Empty;
        _errorLabel.Text = string.Empty;
    }

    /// <summary>Enter, or the Extract button: starts the <see cref="ExtractTask"/> and closes the dialog.</summary>
    private sealed class ExtractCommand(
        IArchiveExtractionService extractionService,
        TaskManager taskManager,
        ProgressWindowFactory progressWindowFactory,
        IContentModelService contentModelService,
        FileSystemResolver fileSystemResolver) : Command
    {
        public override string Title => "Extract";
        public override string Description => "Unpack the archives.";

        public override Key? Key { get; init; } = new Key(ConsoleKey.Enter, false, false, false);

        public override async Task ExecuteAsync(object? args)
        {
            var dialog = Control as ExtractDialog ?? Control?.Parent as ExtractDialog;
            if (dialog is null)
            {
                return;
            }

            var (options, error) = dialog.ReadOptions();
            if (options is null)
            {
                dialog.ErrorMessage.Text = error ?? string.Empty;
                return;
            }

            try
            {
                var target = new Url(dialog.TargetFolder);
                var task = new ExtractTask(extractionService, dialog.Archives, ResolveFileSystem, target, options, dialog.EachIntoOwnFolder);

                // Before the task is queued: a small archive is regularly unpacked before Add returns.
                // A URL inside the target folder, so the panel showing that folder is the one re-read.
                PanelRefreshHelper.RefreshWhenFinished(task, contentModelService, ExtractTask.DestinationFor(target, "x", eachIntoOwnFolder: true));
                TransferCommand.ConsumeMarks(dialog.SourceModel);

                await taskManager.Add(task);
                await dialog.ShowPopUpWindowAsync(progressWindowFactory.Create(task));
                dialog.Close();
                dialog.Reset();
            }
            catch (Exception exception)
            {
                dialog.ErrorMessage.Text = exception.Message;
            }
        }

        private IFileSystem ResolveFileSystem(Url url) => fileSystemResolver.Resolve(url, operation: "Extracting");
    }
}
