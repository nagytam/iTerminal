using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Commands.Marks;
using iTerminal.Core.Commands.Music;
using iTerminal.Core.Controls.Lenses;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using System.ComponentModel;

namespace iTerminal.Core.Controls.Contents;

public class ContentBrowser : iConsole.Controls.Control, IContentBrowser
{
    private TerminalModel _terminalModel { get; set; }
    private TaskManager _taskManager { get; set; }
    public IContentModelService ContentModelService { get; set; }
    public IContentModel? ContentModel { get; set; }
    private ScrollBar ScrollBar { get; set; }
    private IReadOnlyList<IContentLens> _contentLenses { get; set; }
    private CreateFolderDialog? _createFolderDialog { get; set; }
    private CopyDialog? _copyDialog { get; set; }
    private DeleteDialog? _deleteDialog { get; set; }
    private MoveDialog? _moveDialog { get; set; }
    private TextFileEditorDialog? _textFileEditorDialog { get; set; }
    private HelpDialog? _helpDialog { get; set; }
    private FileViewerDialog? _fileViewerDialog { get; set; }
    private iTerminal.Core.Dialogs.Dotnet.SolutionViewerDialog? _solutionViewerDialog { get; set; }
    private iTerminal.Core.Dialogs.Music.MusicFileViewerDialog? _musicFileViewerDialog { get; set; }
    private iTerminal.Core.Dialogs.Video.VideoFileViewerDialog? _videoFileViewerDialog { get; set; }
    private iTerminal.Core.Dialogs.Video.ImageFileViewerDialog? _imageFileViewerDialog { get; set; }
    private XmlViewerDialog? _xmlViewerDialog { get; set; }
    private JsonViewerDialog? _jsonViewerDialog { get; set; }
    private CsvViewerDialog? _csvViewerDialog { get; set; }
    private SqliteViewerDialog? _sqliteViewerDialog { get; set; }
    private iTerminal.Core.Dialogs.Web.WebBrowserDialog? _webBrowserDialog { get; set; }
    private MarkPatternDialog? _markPatternDialog { get; set; }
    private MarkPatternDialog? _unmarkPatternDialog { get; set; }
    private CompressDialog? _compressDialog { get; set; }
    private ExtractDialog? _extractDialog { get; set; }
    private Services.FileAssociations.FileAssociationRunner? _fileAssociations { get; set; }
    private OpenWithDialog? _openWithDialog { get; set; }
    private OpenWithDialog? _editWithDialog { get; set; }
    private CreateLinkDialog? _createLinkDialog { get; set; }
    private EditLinkDialog? _editLinkDialog { get; set; }
    private PropertiesDialog? _propertiesDialog { get; set; }
    private ChecksumDialog? _checksumDialog { get; set; }
    private FileSystemResolver? _fileSystemResolver { get; set; }
    private SortDialog? _sortDialog { get; set; }
    private UserMenuDialog? _userMenuDialog { get; set; }
    private FileDiffDialog? _fileDiffDialog { get; set; }
    private iTerminal.Core.Services.Clipboard.FileClipboardOperations? _clipboard { get; set; }
    private DiskImageDialog? _diskImageDialog { get; set; }
    private SecureWipeDialog? _secureWipeDialog { get; set; }
    private SignatureDialog? _signatureDialog { get; set; }
    private PrintDialog? _printDialog { get; set; }
    private iTerminal.Core.Services.Defender.IMalwareScanService? _malwareScanService { get; set; }
    private INotificationService? _notificationService { get; set; }
    private iTerminal.Core.Services.Git.IGitService? _gitService { get; set; }

    public ContentBrowser(
        string name,
        IContentModelService contentModelService,
        TerminalModel terminalModel,
        TaskManager taskManager,
        IThemeService themeService,
        CreateFolderDialog? createFolderDialog = null,
        CopyDialog? copyDialog = null,
        DeleteDialog? deleteDialog = null,
        MoveDialog? moveDialog = null,
        TextFileEditorDialog? textFileEditorDialog = null,
        HelpDialog? helpDialog = null,
        FileViewerDialog? fileViewerDialog = null,
        iTerminal.Core.Dialogs.Dotnet.SolutionViewerDialog? solutionViewerDialog = null,
        IEnumerable<IContentLens>? contentLenses = null,
        iTerminal.Core.Dialogs.Music.MusicFileViewerDialog? musicFileViewerDialog = null,
        iTerminal.Core.Dialogs.Video.VideoFileViewerDialog? videoFileViewerDialog = null,
        iTerminal.Core.Dialogs.Video.ImageFileViewerDialog? imageFileViewerDialog = null,
        XmlViewerDialog? xmlViewerDialog = null,
        JsonViewerDialog? jsonViewerDialog = null,
        MarkPatternDialog? markPatternDialog = null,
        MarkPatternDialog? unmarkPatternDialog = null,
        CompressDialog? compressDialog = null,
        ExtractDialog? extractDialog = null,
        Services.FileAssociations.FileAssociationRunner? fileAssociations = null,
        OpenWithDialog? openWithDialog = null,
        OpenWithDialog? editWithDialog = null,
        CreateLinkDialog? createLinkDialog = null,
        PropertiesDialog? propertiesDialog = null,
        ChecksumDialog? checksumDialog = null,
        FileSystemResolver? fileSystemResolver = null,
        SortDialog? sortDialog = null,
        UserMenuDialog? userMenuDialog = null,
        FileDiffDialog? fileDiffDialog = null,
        iTerminal.Core.Services.Clipboard.FileClipboardOperations? clipboardOperations = null,
        DiskImageDialog? diskImageDialog = null,
        SecureWipeDialog? secureWipeDialog = null,
        SignatureDialog? signatureDialog = null,
        iTerminal.Core.Services.Defender.IMalwareScanService? malwareScanService = null,
        INotificationService? notificationService = null,
        EditLinkDialog? editLinkDialog = null,
        iTerminal.Core.Services.Git.IGitService? gitService = null,
        CsvViewerDialog? csvViewerDialog = null,
        iTerminal.Core.Dialogs.Web.WebBrowserDialog? webBrowserDialog = null,
        PrintDialog? printDialog = null,
        SqliteViewerDialog? sqliteViewerDialog = null)
        : base(themeService)
    {
        _webBrowserDialog = webBrowserDialog;
        _gitService = gitService;
        _csvViewerDialog = csvViewerDialog;
        _sqliteViewerDialog = sqliteViewerDialog;
        _editLinkDialog = editLinkDialog;
        _malwareScanService = malwareScanService;
        _notificationService = notificationService;
        _diskImageDialog = diskImageDialog;
        _secureWipeDialog = secureWipeDialog;
        _signatureDialog = signatureDialog;
        _printDialog = printDialog;
        _clipboard = clipboardOperations;
        _fileDiffDialog = fileDiffDialog;
        Name = name;
        _sortDialog = sortDialog;
        _userMenuDialog = userMenuDialog;
        _propertiesDialog = propertiesDialog;
        _checksumDialog = checksumDialog;
        _fileSystemResolver = fileSystemResolver;
        _createLinkDialog = createLinkDialog;
        _fileAssociations = fileAssociations;
        _openWithDialog = openWithDialog;
        _editWithDialog = editWithDialog;
        ContentModelService = contentModelService ?? NullContentModelService.Instance;
        ContentModelService.ModelChanged += OnContentModelServiceModelChanged;
        _terminalModel = terminalModel ?? new TerminalModel(new iConsole.Tasks.TaskManager(), NullContentModelService.Instance);
        _taskManager = taskManager ?? new TaskManager();
        _contentLenses = contentLenses?.ToArray() ?? [];
        _createFolderDialog = createFolderDialog;
        _copyDialog = copyDialog;
        _deleteDialog = deleteDialog;
        _moveDialog = moveDialog;
        _textFileEditorDialog = textFileEditorDialog;
        _helpDialog = helpDialog;
        _fileViewerDialog = fileViewerDialog;
        _solutionViewerDialog = solutionViewerDialog;
        _musicFileViewerDialog = musicFileViewerDialog;
        _videoFileViewerDialog = videoFileViewerDialog;
        _imageFileViewerDialog = imageFileViewerDialog;
        _xmlViewerDialog = xmlViewerDialog;
        _jsonViewerDialog = jsonViewerDialog;
        _markPatternDialog = markPatternDialog;
        _unmarkPatternDialog = unmarkPatternDialog;
        _compressDialog = compressDialog;
        _extractDialog = extractDialog;
        ViewSize = new Size(0, 0);
        CanFocus = true;
        ScrollBar = new ScrollBar(themeService);
        AddControl(ScrollBar);
    }

    private void OnContentModelServiceModelChanged(object? sender, EventArgs e)
    {
        StateHasChanged();
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        if (ContentModel is not null)
        {
            // -= first: this runs again on every model change, and a second subscription repainted
            // the panel twice for every change of the model.
            ContentModel.PropertyChanged -= OnContentModelChanged;
            ContentModel.PropertyChanged += OnContentModelChanged;
            ScrollBar.ScrollableModel = ContentModel;
        }
    }

    public override void OnInitializeCommands()
    {
        ArgumentNullException.ThrowIfNull(ContentModel);
        ArgumentNullException.ThrowIfNull(ContentModelService);

        base.OnInitializeCommands();

        // The music commands go first so Enter, F5 and Delete mean the music thing on a music drive.
        // Each is inert unless this browser's model is the focused music model, so on every other
        // drive the file-system commands below behave exactly as they always have.
        AddCommand(new ActivateMusicEntryCommand(ContentModel, ContentModelService));
        AddCommand(new ToggleMusicSelectionCommand(ContentModel, ContentModelService));
        AddCommand(new AddToMusicPlaylistCommand(ContentModel, ContentModelService));
        AddCommand(new RemoveFromMusicPlaylistCommand(ContentModel, ContentModelService));
        AddCommand(new MusicPlayPauseCommand(ContentModel, ContentModelService));
        AddCommand(new MusicVolumeUpCommand(ContentModel, ContentModelService));
        AddCommand(new MusicVolumeDownCommand(ContentModel, ContentModelService));

        // Enter runs what is on the command line before it opens anything; Alt+Enter, Ctrl+Enter,
        // Shift+Enter and Ctrl+Shift+Enter copy names to it. Ctrl+X T/P of mc is not available:
        // the password manager has Ctrl+X.
        AddCommand(new iTerminal.Core.Commands.CommandLine.RunCommandLineCommand(ContentModel, ContentModelService, _terminalModel, IsCommandLineVisible));
        void Reveal() => FindContent()?.RevealCommandLine();
        AddCommand(new iTerminal.Core.Commands.CommandLine.InsertIntoCommandLineCommand(
            iTerminal.Core.Commands.CommandLine.CommandLineInsert.FileName, new Key(ConsoleKey.Enter, false, true, false),
            ContentModel, ContentModelService, _terminalModel, Reveal));
        AddCommand(new iTerminal.Core.Commands.CommandLine.InsertIntoCommandLineCommand(
            iTerminal.Core.Commands.CommandLine.CommandLineInsert.FileName, new Key(ConsoleKey.Enter, false, false, true),
            ContentModel, ContentModelService, _terminalModel, Reveal));
        AddCommand(new iTerminal.Core.Commands.CommandLine.InsertIntoCommandLineCommand(
            iTerminal.Core.Commands.CommandLine.CommandLineInsert.Marked, new Key(ConsoleKey.Enter, true, false, false),
            ContentModel, ContentModelService, _terminalModel, Reveal));
        AddCommand(new iTerminal.Core.Commands.CommandLine.InsertIntoCommandLineCommand(
            iTerminal.Core.Commands.CommandLine.CommandLineInsert.FolderPath, new Key(ConsoleKey.Enter, true, false, true),
            ContentModel, ContentModelService, _terminalModel, Reveal));

        // Operating a container from its row on the docker:// root; inert anywhere else.
        AddCommand(new Commands.Docker.StartStopContainerCommand(ContentModel, ContentModelService));
        AddCommand(new Commands.Docker.RestartContainerCommand(ContentModel, ContentModelService));

        // Marking rows. After the music commands, so Insert on the catalog keeps marking albums
        // there; each is inert unless this browser's model is the focused one.
        AddCommand(new ToggleMarkCommand(ContentModelService, ContentModel));
        AddCommand(new MarkAndMoveCommand(ContentModelService, 1, ContentModel));
        AddCommand(new MarkAndMoveCommand(ContentModelService, -1, ContentModel));
        AddCommand(new iTerminal.Core.Commands.Listing.ToggleListingModeCommand(ContentModelService, this));
        AddCommand(new iTerminal.Core.Commands.Listing.ListingModeLeftCommand(this));
        AddCommand(new iTerminal.Core.Commands.Listing.ListingModeRightCommand(this));
        AddCommand(new iTerminal.Core.Commands.Listing.ToggleLongListingCommand(ContentModelService, this));
        AddCommand(new iTerminal.Core.Commands.Listing.ToggleInfoListingCommand(ContentModelService, this));
        AddCommand(new InvertMarksCommand(ContentModelService, ContentModel));
        if (_markPatternDialog is not null)
        {
            AddCommand(new MarkByPatternDialogCommand(_markPatternDialog, ContentModelService, marking: true, ContentModel));
        }
        if (_unmarkPatternDialog is not null)
        {
            AddCommand(new MarkByPatternDialogCommand(_unmarkPatternDialog, ContentModelService, marking: false, ContentModel));
        }

        // The system clipboard: Ctrl+C/X/V as in Explorer, with the CUA twins for a terminal that keeps
        // one of them for itself. Each declines on ".." and where there is nothing to act on, which
        // leaves Ctrl+C to quit and Ctrl+X to the password manager there.
        if (_clipboard is not null)
        {
            AddCommand(new Commands.Clipboard.ClipboardCopyCommand(_clipboard, ContentModelService, ContentModel));
            AddCommand(new Commands.Clipboard.ClipboardCopyCommand(_clipboard, ContentModelService, ContentModel, new Key(ConsoleKey.Insert, false, false, true)));
            AddCommand(new Commands.Clipboard.ClipboardCutCommand(_clipboard, ContentModelService, ContentModel));
            AddCommand(new Commands.Clipboard.ClipboardCutCommand(_clipboard, ContentModelService, ContentModel, new Key(ConsoleKey.Delete, true, false, false)));
            AddCommand(new Commands.Clipboard.ClipboardPasteCommand(_clipboard, ContentModelService, ContentModel));
            AddCommand(new Commands.Clipboard.ClipboardPasteCommand(_clipboard, ContentModelService, ContentModel, new Key(ConsoleKey.Insert, true, false, false)));
        }

        // Enter on the recycle drive restores; inert anywhere else, and on that drive's ".." row.
        AddCommand(new Commands.Drives.RestoreRecycledCommand(ContentModelService));

        AddCommand(new NavigateToParentFolderCommand(ContentModel, ContentModelService));
        CommandHelper.AddVerticalScrollingCommands(this, ContentModel, true);
        AddCommand(new NavigateToFolderCommand(ContentModel, ContentModelService));
        AddCommand(new RefreshCommand(ContentModel));
        AddCommand(new CalculateFolderSizeCommand(ContentModelService, ContentModel));
        AddCommand(new NavigateToRootFolderCommand(ContentModel));
        // Ahead of the other things Enter does to a file — open an archive as a folder, run it in
        // the command log: a program the user chose for the file is what they meant by Enter.
        if (_fileAssociations is not null)
        {
            AddCommand(new OpenAssociatedFileCommand(ContentModelService, _fileAssociations));
        }
        AddCommand(new NavigateToArchiveCommand(ContentModelService));
        AddCommand(new TabForwardCommand());
        AddCommand(new TabBackwardCommand());
        AddCommand(new ExecuteCommand(ContentModel, _terminalModel, _taskManager));
        // Any other file, with no program chosen: the system's default program, outside the application.
        if (_fileAssociations is not null)
        {
            AddCommand(new OpenWithSystemDefaultCommand(ContentModelService, _fileAssociations));
        }
        if (_createFolderDialog is not null)
        {
            AddCommand(new CreateFolderDialogCommand(_createFolderDialog, ContentModelService));
        }
        if (_copyDialog is not null)
        {
            AddCommand(new CopyDialogCommand(_copyDialog, ContentModelService));
        }
        if (_createLinkDialog is not null)
        {
            AddCommand(new CreateLinkDialogCommand(_createLinkDialog, ContentModelService));
        }
        if (_editLinkDialog is not null)
        {
            AddCommand(new EditLinkDialogCommand(_editLinkDialog, ContentModelService));
        }
        if (_diskImageDialog is not null)
        {
            AddCommand(new MountDiskImageDialogCommand(_diskImageDialog, ContentModelService));
        }
        if (_userMenuDialog is not null)
        {
            AddCommand(new UserMenuDialogCommand(_userMenuDialog, ContentModelService));
        }

        if (_sortDialog is not null)
        {
            AddCommand(new SortDialogCommand(_sortDialog, ContentModelService));
        }
        if (_propertiesDialog is not null)
        {
            AddCommand(new PropertiesDialogCommand(_propertiesDialog, ContentModelService));
        }
        if (_checksumDialog is not null)
        {
            AddCommand(new ChecksumDialogCommand(_checksumDialog, ContentModelService, _fileSystemResolver));
        }
        if (_secureWipeDialog is not null)
        {
            AddCommand(new SecureWipeDialogCommand(_secureWipeDialog, ContentModelService));
        }
        if (_malwareScanService is { IsAvailable: true })
        {
            AddCommand(new ScanWithDefenderCommand(ContentModelService, _malwareScanService, _notificationService));
        }
        if (_signatureDialog is not null)
        {
            AddCommand(new SignatureDialogCommand(_signatureDialog, ContentModelService));
        }
        if (_printDialog is not null)
        {
            AddCommand(new PrintDialogCommand(_printDialog, ContentModelService));
        }
        if (_gitService is not null)
        {
            AddCommand(new GitPullRebaseCommand(ContentModelService, _gitService, _notificationService));
            AddCommand(new GitSyncCommand(ContentModelService, _gitService, _notificationService));
        }
        if (_fileDiffDialog is not null)
        {
            AddCommand(new DiffFilesDialogCommand(_fileDiffDialog, ContentModelService, _fileSystemResolver));
        }
        if (_deleteDialog is not null)
        {
            AddCommand(new DeleteDialogCommand(_deleteDialog, ContentModelService));
        }
        if (_moveDialog is not null)
        {
            AddCommand(new MoveDialogCommand(_moveDialog, ContentModelService));
        }
        if (_compressDialog is not null)
        {
            AddCommand(new CompressDialogCommand(_compressDialog, ContentModelService));
        }
        if (_extractDialog is not null)
        {
            AddCommand(new ExtractDialogCommand(_extractDialog, ContentModelService));
        }
        if (_fileViewerDialog is not null)
        {
            AddCommand(new ViewFileDialogCommand(_fileViewerDialog, ContentModelService, _solutionViewerDialog, _musicFileViewerDialog, _videoFileViewerDialog, _imageFileViewerDialog, _xmlViewerDialog, _jsonViewerDialog, _csvViewerDialog, _webBrowserDialog, sqliteViewerDialog: _sqliteViewerDialog));
        }
        if (_textFileEditorDialog is not null)
        {
            AddCommand(new EditTextFileDialogCommand(_textFileEditorDialog, ContentModelService, _fileAssociations));
        }
        if (_openWithDialog is not null)
        {
            AddCommand(new OpenWithDialogCommand(_openWithDialog, ContentModelService, Services.FileAssociations.FileAssociationKind.Open));
        }
        if (_editWithDialog is not null)
        {
            AddCommand(new OpenWithDialogCommand(_editWithDialog, ContentModelService, Services.FileAssociations.FileAssociationKind.Edit, _textFileEditorDialog));
        }
        if (_helpDialog is not null)
        {
            AddCommand(new HelpDialogCommand(_helpDialog));
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent is not null && ContentModel is not null)
        {
            var contentWidth = Math.Max(0, ViewSize.Width - 2);
            var contentHeight = Math.Max(0, ViewSize.Height - 2);
            ContentModel.ViewSize = new Size(contentWidth, contentHeight);
            ApplyListingLayout();
        }

        ScrollBar.Position = new Position(Position.X + ViewSize.Width - 1, Position.Y + 1);
        ScrollBar.ViewSize = new Size(1, Math.Max(0, ViewSize.Height - 2));
    }

    private void OnContentModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var visualState = GetVisualState();
        var contentModel = ContentModel;

        string header = Name;
        var footer = string.Empty;
        if (contentModel is not null)
        {
            header = contentModel.GetHeader((int)ViewSize.Width - 3);

            // Shrunk to the border it is drawn into, because a footer that does not fit is not
            // truncated by the border — it is dropped whole (AlignCenter only writes a title
            // shorter than the line), and a panel silently loses its counts and its key hints.
            // GetFooter has no width to work with; the control that draws it does.
            footer = contentModel.GetFooter().Shrink((int)ViewSize.Width - 3) ?? string.Empty;
            header = BuildHeaderWithLensIndicators(contentModel, header);
        }

        DrawBorder(buffer, ZOrder, AbsolutePosition, ViewSize, header, footer, colorScheme, visualState);
        base.Draw(buffer);

        if (contentModel is not null && IsInfoListing)
        {
            DrawInfoRows(buffer, colorScheme);
            return;
        }

        if (contentModel is not null)
        {
            var columns = ScrollableModel.ColumnCountOf(contentModel);
            if (columns > 1)
            {
                DrawBriefRows(buffer, contentModel, columns, colorScheme, visualState);
                ScrollBar.Draw(buffer);
                return;
            }

            var filteredEntries = contentModel.FilteredEntries;
            var width = Math.Max(0, (int)ViewSize.Width);
            var rowCount = Math.Max(0, (int)ViewSize.Height - 2);
            var size = new Size(Math.Max(0, width - 2), 1);
            var positionX = AbsolutePosition.X + 1;
            var positionY = AbsolutePosition.Y + 1;
            var scrollPositionY = (int)contentModel.ScrollPosition.Y;
            var cursorPositionY = (int)contentModel.CursorPosition.Y;
            var visibleEntryCount = Math.Min(rowCount, Math.Max(0, filteredEntries.Count - scrollPositionY));

            for (var y = 0; y < visibleEntryCount; y++)
            {
                var entryIndex = scrollPositionY + y;
                var entry = filteredEntries[entryIndex];
                var isCursor = entryIndex == cursorPositionY;
                var text = IsLongListing
                    ? contentModel.FormatLongItemName(entry, width, isCursor, colorScheme, visualState)
                    : contentModel.FormatItemName(entry, width, isCursor, colorScheme, visualState);
                DecorateRow(contentModel, entry, text, isCursor, colorScheme, visualState);
                if (contentModel is IMarkableContentModel marks && marks.IsMarked(entry))
                {
                    PaintMarked(text, isCursor);
                }
                PadRow(text, (int)size.Width, colorScheme);
                buffer.Write(ZOrder,
                    new Position(positionX, positionY + y),
                    size,
                    text);
            }

            for (var y = visibleEntryCount; y < rowCount; y++)
            {
                var text = IsLongListing
                    ? contentModel.FormatLongItemName(null, width, false, colorScheme, visualState)
                    : contentModel.FormatItemName(null, width, false, colorScheme, visualState);
                DecorateRow(contentModel, null, text, false, colorScheme, visualState);
                buffer.Write(ZOrder,
                    new Position(positionX, positionY + y),
                    size,
                    text);
            }

            ScrollBar.Draw(buffer);
        }
    }

    /// <summary>
    /// A row shorter than the panel would leave the previous frame's cells beside it — the layer is
    /// never cleared between frames — so whatever an entry formats, the row is padded to the full
    /// width, in the colours of its last cell so a cursor row stays one bar.
    /// </summary>
    internal static void PadRow(ConxelString row, int width, ColorScheme colorScheme)
    {
        if (row.Count >= width)
        {
            return;
        }

        var foreground = row.Count > 0 ? row.String[^1].ForegroundColor : colorScheme.Foreground;
        var background = row.Count > 0 ? row.String[^1].BackgroundColor : colorScheme.Background;
        row.Expand(width, foreground, background);
    }

    private string BuildHeaderWithLensIndicators(IContentModel contentModel, string header)
    {
        if (_contentLenses.Count == 0)
        {
            return header;
        }

        List<string> indicators = [];
        foreach (var lens in _contentLenses)
        {
            var indicator = lens.GetHeaderIndicator(contentModel);
            if (!string.IsNullOrWhiteSpace(indicator))
            {
                indicators.Add(indicator);
            }
        }

        if (indicators.Count == 0)
        {
            return header;
        }

        return string.Join(' ', indicators) + " " + header;
    }

    /// <summary>
    /// A marked row is drawn in yellow — the colour every commander marks with — and, under the
    /// cursor, whose background is yellow already, in dark red. Painted after the lenses so a git
    /// status colour cannot hide a mark: the mark is what the next F5 or F8 will act on.
    /// </summary>
    /// <summary>The narrowest name column a Brief listing will draw; the column count is the inner width over this.</summary>
    internal const int MinimumBriefCellWidth = 16;

    /// <summary>
    /// The listing a file panel shows. Full is the one-column row (name, size, date, time). Brief is names in
    /// columns (Ctrl+T). Long is the one-column row with an attributes column and no size (palette). Info shows
    /// the other panel's cursor entry as label/value rows and is read-only here (palette; Esc leaves it).
    /// </summary>
    public enum PanelListingMode
    {
        Full,
        Brief,
        Long,
        Info,
    }

    /// <summary>The mode lives on this control, so it survives a model swap (except onto Preview).</summary>
    public PanelListingMode ListingMode { get; private set; } = PanelListingMode.Full;

    public bool IsBriefListing => ListingMode == PanelListingMode.Brief;

    public bool IsLongListing => ListingMode == PanelListingMode.Long;

    public bool IsInfoListing => ListingMode == PanelListingMode.Info;

    /// <summary>Toggles Brief. The Preview panel does not offer it: its entries are text lines.</summary>
    public void ToggleBriefListing() => ToggleListingMode(PanelListingMode.Brief);

    /// <summary>Toggles Long. The Preview panel does not offer it.</summary>
    public void ToggleLongListing() => ToggleListingMode(PanelListingMode.Long);

    /// <summary>Toggles Info. The Preview panel does not offer it.</summary>
    public void ToggleInfoListing() => ToggleListingMode(PanelListingMode.Info);

    private void ToggleListingMode(PanelListingMode mode)
    {
        if (ContentModel is null || ContentModel is ContentModelPreview)
        {
            return;
        }

        ListingMode = ListingMode == mode ? PanelListingMode.Full : mode;
        ApplyListingLayout();
        RefreshInfoRows();
        ContentModel.Update();
        StateHasChanged();
    }

    /// <summary>
    /// Sets the model's column count from the mode and the control's width, and keeps the cursor in view on
    /// the line grid. Called from the layout pass, the toggle and a model swap, never from <see cref="Draw"/>.
    /// With one column nothing is written to the scroll position, so Long mode is unchanged.
    /// </summary>
    internal void ApplyListingLayout()
    {
        if (ContentModel is not IColumnScrollableModel model)
        {
            return;
        }

        var innerWidth = Math.Max(0, (int)ViewSize.Width - 2);
        var columns = IsBriefListing ? Math.Max(1, innerWidth / MinimumBriefCellWidth) : 1;
        model.ColumnCount = columns;
        if (columns > 1)
        {
            model.AlignScroll();
            ScrollableModel.ScrollLinesToVisible(model);
        }
    }

    /// <summary>Left or Right inside the line of the cursor (−1 / +1). Stays put at the line's ends.</summary>
    public void MoveWithinLine(int step)
    {
        var model = ContentModel;
        if (model is null || ScrollableModel.ColumnCountOf(model) <= 1)
        {
            return;
        }

        var columns = ScrollableModel.ColumnCountOf(model);
        var count = model.FilteredEntries.Count;
        if (count <= 0)
        {
            return;
        }

        var cursor = (int)model.CursorPosition.Y;
        var target = Math.Clamp(cursor + step, 0, count - 1);
        if (target == cursor || target / columns != cursor / columns)
        {
            return;
        }

        model.CursorPosition.Y = target;
        ScrollableModel.ScrollLinesToVisible(model);
        model.Update();
        StateHasChanged();
    }

    private IReadOnlyList<ListingInfoRow> _infoRows = [];

    /// <summary>
    /// Polled from the UI thread: the Info rows follow the other panel's cursor. The read is a plain index into its
    /// in-memory list; a bad index is treated as no entry. The rows are only redrawn when a field changed.
    /// </summary>
    public override void OnIdle()
    {
        base.OnIdle();
        if (IsInfoListing)
        {
            RefreshInfoRows();
        }
    }

    private void RefreshInfoRows()
    {
        if (!IsInfoListing)
        {
            return;
        }

        var ownName = ContentModel?.Name;
        var otherName = ownName == Services.ContentModelService.Left ? Services.ContentModelService.Right : Services.ContentModelService.Left;
        var other = ContentModelService.ContentModels?.SingleOrDefault(m => m.Name == otherName);
        IEntry? entry = null;
        try
        {
            if (other is not null)
            {
                var index = (int)other.CursorPosition.Y;
                var entries = other.FilteredEntries;
                if (index >= 0 && index < entries.Count)
                {
                    entry = entries[index];
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            entry = null;
        }

        var rows = ListingInfo.Describe(entry, key => UiText.Get(key, key));
        if (!rows.SequenceEqual(_infoRows))
        {
            _infoRows = rows;
            StateHasChanged();
        }
    }

    /// <summary>Draws the Info rows: a label column, then the value, one row per line. Every row is written in full.</summary>
    private void DrawInfoRows(IBuffer buffer, ColorScheme colorScheme)
    {
        var innerWidth = Math.Max(0, (int)ViewSize.Width - 2);
        var rows = Math.Max(0, (int)ViewSize.Height - 2);
        var labelWidth = Math.Min(12, innerWidth / 3);
        var positionX = AbsolutePosition.X + 1;
        var positionY = AbsolutePosition.Y + 1;

        for (var y = 0; y < rows; y++)
        {
            var text = new ConxelString();
            if (y < _infoRows.Count)
            {
                var row = _infoRows[y];
                var line = row.Label.PadRight(labelWidth) + row.Value;
                if (line.Length > innerWidth)
                {
                    line = line[..innerWidth];
                }

                text.Append(line, colorScheme.Foreground, colorScheme.Background);
            }

            PadRow(text, innerWidth, colorScheme);
            buffer.Write(ZOrder, new Position(positionX, positionY + y), new Size(innerWidth, 1), text);
        }
    }

    /// <summary>
    /// Draws the Brief cells: <paramref name="columns"/> cells per row, row-major, the last column absorbing the
    /// width left over. Every cell is written in full, so nothing from the previous frame shows through.
    /// </summary>
    private void DrawBriefRows(IBuffer buffer, IContentModel contentModel, int columns, ColorScheme colorScheme, VisualState visualState)
    {
        var filteredEntries = contentModel.FilteredEntries;
        var innerWidth = Math.Max(0, (int)ViewSize.Width - 2);
        var rows = Math.Max(0, (int)ViewSize.Height - 2);
        var cellWidth = innerWidth / columns;
        // The read is floored: a scroll position that is not on a line start is drawn from its line.
        var firstLine = (int)contentModel.ScrollPosition.Y / columns;
        var cursor = (int)contentModel.CursorPosition.Y;
        var count = filteredEntries.Count;
        var positionX = AbsolutePosition.X + 1;
        var positionY = AbsolutePosition.Y + 1;

        for (var row = 0; row < rows; row++)
        {
            var x = 0;
            for (var column = 0; column < columns; column++)
            {
                var isLast = column == columns - 1;
                var width = isLast ? innerWidth - cellWidth * (columns - 1) : cellWidth;
                var index = (firstLine + row) * columns + column;
                var entry = index < count ? filteredEntries[index] : null;
                var isCursor = entry is not null && index == cursor;

                // The separator is part of the cell's width, so the name gets one column less than the cell.
                var text = contentModel.FormatBriefItemName(entry, isLast ? width : width - 1, isCursor, colorScheme, visualState);
                DecorateRow(contentModel, entry, text, isCursor, colorScheme, visualState);
                if (entry is not null && contentModel is IMarkableContentModel marks && marks.IsMarked(entry))
                {
                    PaintMarked(text, isCursor);
                }

                if (!isLast)
                {
                    text.Append(Border.VerticalSingle, colorScheme.Foreground, colorScheme.Background);
                }

                PadRow(text, width, colorScheme);
                buffer.Write(ZOrder,
                    new Position(positionX + x, positionY + row),
                    new Size(width, 1),
                    text);
                x += width;
            }
        }
    }

    /// <summary>
    /// The entry under a point of the list, in the current layout. In Brief it is the cell's entry, which may be
    /// past the last entry; in Long it is the row's entry.
    /// </summary>
    private int IndexAtListPoint(int lx, int row)
    {
        var model = ContentModel!;
        var columns = ScrollableModel.ColumnCountOf(model);
        if (columns <= 1)
        {
            return (int)(model.ScrollPosition.Y + row);
        }

        var innerWidth = Math.Max(0, (int)ViewSize.Width - 2);
        var cellWidth = Math.Max(1, innerWidth / columns);
        var column = Math.Min((lx - 1) / cellWidth, columns - 1);
        var firstLine = (int)model.ScrollPosition.Y / columns;
        return (firstLine + row) * columns + column;
    }

    /// <summary>A wheel notch scrolls one line of Brief: the cursor and the view move together.</summary>
    private void ScrollByWheelLines(int wheelDelta, int columns, int itemCount)
    {
        var model = ContentModel!;
        var step = wheelDelta * columns;
        var targetCursor = Math.Clamp((int)model.CursorPosition.Y - step, 0, itemCount - 1);
        var lines = (itemCount + columns - 1) / columns;
        var viewHeight = Math.Max(0, (int)model.ViewSize.Height);
        var maxScroll = Math.Max(0, lines - viewHeight) * columns;
        var targetScroll = Math.Clamp((int)model.ScrollPosition.Y - step, 0, maxScroll);

        model.ScrollPosition = new Position(model.ScrollPosition.X, targetScroll);
        model.CursorPosition = new Position(model.CursorPosition.X, targetCursor);
        ScrollableModel.ScrollLinesToVisible(model);
        model.Update();
    }

    internal static void PaintMarked(ConxelString row, bool isCursor)
    {
        var color = isCursor ? Color.DarkRed : Color.Yellow;
        for (var i = 0; i < row.Count; i++)
        {
            var cell = row.String[i];
            cell.ForegroundColor = color;
            cell.IsBold = true;
            row.String[i] = cell;
        }
    }

    private void DecorateRow(
        IContentModel contentModel,
        IEntry? entry,
        ConxelString row,
        bool isCursor,
        ColorScheme colorScheme,
        VisualState visualState)
    {
        foreach (var lens in _contentLenses)
        {
            lens.DecorateRow(contentModel, entry, row, isCursor, colorScheme, visualState);
        }
    }

    public override void OnHandleTabForward()
    {
        var parentPanel = Parent?.Parent;
        if (parentPanel != null)
        {
            var focusableControls = parentPanel.Controls.Where(c => (c is FilePanel || c is TerminalWindow || c is AiPromptBar || c is TerminalPanel) && c.IsVisible).ToList();
            var index = focusableControls.IndexOf(Parent!);
            var nextIndex = (index + 1) % focusableControls.Count;
            var nextPanel = focusableControls[nextIndex];
            if (nextPanel != null)
            {
                nextPanel?.SetFocus();
            }
        }
    }

    public override void OnHandleTabBackward()
    {
        var parentPanel = Parent?.Parent;
        if (parentPanel != null)
        {
            var focusableControls = parentPanel.Controls.Where(c => (c is FilePanel || c is TerminalWindow || c is AiPromptBar || c is TerminalPanel) && c.IsVisible).ToList();
            var index = focusableControls.IndexOf(Parent!);
            var nextIndex = (index - 1 + focusableControls.Count) % focusableControls.Count;
            var nextPanel = focusableControls[nextIndex];
            nextPanel?.SetFocus();
        }
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (ContentModel is null)
        {
            return await base.HandleMouseEventAsync(mouseEvent);
        }

        var hasAction = mouseEvent.WheelDelta != 0
            || mouseEvent.LeftClicked
            || mouseEvent.RightClicked;
        if (!hasAction || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return await base.HandleMouseEventAsync(mouseEvent);
        }

        var lx = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
        var ly = (int)mouseEvent.Position.Y - (int)AbsolutePosition.Y;

        if (IsInfoListing)
        {
            // Info shows another panel's entry: a click or the wheel here changes nothing, and the hidden cursor stays put.
            SetFocus();
            return true;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            SetFocus();
            ScrollByWheelDelta(mouseEvent.WheelDelta);
            StateHasChanged();
            return true;
        }

        SetFocus();

        if (IsScrollBarHit(lx, ly))
        {
            await HandleScrollBarClickAsync(lx, ly);
            StateHasChanged();
            return true;
        }

        if (IsListHit(lx, ly))
        {
            var row = ly - 1;
            var filteredCount = ContentModel.FilteredEntries.Count;
            var openEntry = false;
            if (filteredCount > 0)
            {
                var index = IndexAtListPoint(lx, row);
                // In Brief a click on an empty cell selects nothing; Long clamps to the last row.
                var isBriefEmptyCell = ScrollableModel.ColumnCountOf(ContentModel) > 1 && index >= filteredCount;
                if (!isBriefEmptyCell)
                {
                    index = Math.Clamp(index, 0, filteredCount - 1);
                    openEntry = mouseEvent.LeftClicked && IsDoubleClick(index);
                    ContentModel.CursorPosition.Y = (uint)index;
                    ContentModel.ScrollToVisible();
                    ContentModel.Update();
                }
            }

            StateHasChanged();

            if (mouseEvent.RightClicked && filteredCount > 0)
            {
                // The row is under the cursor now, so every command the menu offers acts on it.
                await ShowContextMenuAsync(mouseEvent.Position);
                return true;
            }

            if (openEntry)
            {
                // A second click on the same row opens it — exactly what Enter does on that row,
                // so a folder is entered, an archive opened, a file executed.
                await HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
            }

            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>
    /// The right-click menu: what the panel can do with the row under the cursor, as a list that opens
    /// at the pointer. Choosing a row runs the command after the menu has closed and handed focus
    /// back, so the command finds the panel focused as it would for its key.
    /// </summary>
    internal async Task ShowContextMenuAsync(Position position)
    {
        var items = FileContextMenu.Build(
            ContentModel,
            Commands.AsSnapshot(),
            _fileAssociations,
            () => HandleKeyEventAsync(new KeyEvent((char)13, ConsoleKey.Enter, false, false, false)));
        if (items.Count == 0)
        {
            return;
        }

        Popup? popup = null;
        var list = new PopupListView(ThemeService)
        {
            Committed = index =>
            {
                popup?.Close();
                _ = RunMenuItemAsync(items[index]);
            },
            Cancelled = () => popup?.Close(),
        };
        list.SetItems([.. items.Select(i => i.Label)]);

        var width = items.Max(i => i.Label.Length) + 3;
        popup = new Popup(ThemeService)
        {
            Placement = PopupPlacement.Manual,
            Anchor = new Position(Math.Max(0, (int)position.X), Math.Max(0, (int)position.Y)),
            HasBorder = true,
            ViewSize = new Size(width + 2, items.Count + 2),
        };
        popup.SetContent(list);

        await popup.ShowAsync(this);
    }

    private static async Task RunMenuItemAsync(ContextMenuItem item)
    {
        try
        {
            await item.Run();
        }
        catch
        {
            // A command contains its own failures; this only keeps a stray one off the input loop.
        }
    }

    /// <summary>Two clicks on one row within this long are a double-click.</summary>
    internal static readonly TimeSpan DoubleClickInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Clock for double-click detection, in milliseconds; replaceable so a test need not race it.</summary>
    internal Func<long> TickSource { get; set; } = () => Environment.TickCount64;

    private long _lastClickTick = long.MinValue;
    private int _lastClickIndex = -1;

    private bool IsDoubleClick(int index)
    {
        var now = TickSource();
        var isDouble = index == _lastClickIndex
            && _lastClickTick != long.MinValue
            && now - _lastClickTick <= (long)DoubleClickInterval.TotalMilliseconds;

        // A double-click consumes both clicks, so a third click starts a new pair rather than
        // opening the entry that was just opened a second time.
        _lastClickTick = isDouble ? long.MinValue : now;
        _lastClickIndex = isDouble ? -1 : index;
        return isDouble;
    }

    private void ScrollByWheelDelta(int wheelDelta)
    {
        if (ContentModel is null || wheelDelta == 0)
        {
            return;
        }

        var itemCount = ContentModel.FilteredEntries.Count;
        if (itemCount <= 0)
        {
            ContentModel.ScrollPosition = new Position(0, 0);
            ContentModel.CursorPosition = new Position(0, 0);
            ContentModel.Update();
            return;
        }

        var columns = ScrollableModel.ColumnCountOf(ContentModel);
        if (columns > 1)
        {
            ScrollByWheelLines(wheelDelta, columns, itemCount);
            return;
        }

        var maxIndex = itemCount - 1;
        var targetCursor = Math.Clamp(
            (int)ContentModel.CursorPosition.Y - wheelDelta,
            0,
            maxIndex);

        var viewHeight = Math.Max(1, (int)ContentModel.ViewSize.Height);
        var targetScroll = (int)ContentModel.ScrollPosition.Y;
        if (targetCursor < targetScroll)
        {
            targetScroll = targetCursor;
        }
        else if (targetCursor > targetScroll + viewHeight - 1)
        {
            targetScroll = targetCursor - viewHeight + 1;
        }

        targetScroll = Math.Clamp(targetScroll, 0, Math.Max(0, itemCount - viewHeight));

        ContentModel.ScrollPosition = new Position(ContentModel.ScrollPosition.X, targetScroll);
        ContentModel.CursorPosition = new Position(ContentModel.CursorPosition.X, targetCursor);
        ContentModel.Update();
    }

    private bool IsScrollBarHit(int lx, int ly) =>
        lx == (int)ViewSize.Width - 1
        && ly >= 1
        && ly <= (int)ViewSize.Height - 2;

    private bool IsListHit(int lx, int ly) =>
        lx >= 1
        && lx <= (int)ViewSize.Width - 2
        && ly >= 1
        && ly <= (int)ViewSize.Height - 2;

    private async Task HandleScrollBarClickAsync(int lx, int ly)
    {
        _ = lx;
        var sbH = (int)ViewSize.Height - 2;
        if (sbH < 1 || ContentModel is null)
        {
            return;
        }

        var sy = ly - 1;
        if (sy < 0 || sy >= sbH)
        {
            return;
        }

        if (sbH < 3)
        {
            await new ScrollDownCommand(ContentModel).RunAsync(null);
            return;
        }

        if (sy == 0)
        {
            await new ScrollUpCommand(ContentModel).RunAsync(null);
            return;
        }

        if (sy == sbH - 1)
        {
            await new ScrollDownCommand(ContentModel).RunAsync(null);
            return;
        }

        var trackH = sbH - 2;
        if (trackH <= 0)
        {
            return;
        }

        var rel = (sy - 1) / (double)trackH;
        var max = Math.Max(0, (int)ContentModel.ScrollableSize.Height - 1);
        var target = (int)Math.Round(rel * max);
        target = Math.Clamp(target, 0, max);
        ContentModel.CursorPosition.Y = (uint)target;
        ContentModel.ScrollToVisible();
        ContentModel.Update();
    }

    /// <summary>The <see cref="Content"/> this panel is shown in, or null in a test that built the panel alone.</summary>
    private Content? FindContent()
    {
        IControl? control = Parent;
        while (control is not null && control is not Content)
        {
            control = control.Parent;
        }

        return control as Content;
    }

    private bool IsCommandLineVisible() => FindContent()?.IsCommandLineVisible ?? false;

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // Info shows another panel's entry, so keys that would act on this panel's hidden cursor are swallowed here.
        // Tab, the menus and the navigation keys still work, and Esc gives the list back.
        if (IsInfoListing)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
            {
                ListingMode = PanelListingMode.Full;
                ApplyListingLayout();
                StateHasChanged();
                return true;
            }

            if (keyEvent.ConsoleKey is ConsoleKey.Enter or ConsoleKey.Spacebar or ConsoleKey.Insert or ConsoleKey.Delete or ConsoleKey.Backspace
                || (keyEvent.ConsoleKey is >= ConsoleKey.F2 and <= ConsoleKey.F9)
                || keyEvent.ConsoleKey is ConsoleKey.F11 or ConsoleKey.F12)
            {
                return true;
            }
        }

        // Alt+P / Alt+N walk the command history, as in mc, where Alt+letter is not a search
        // either. They come before the search below, so a search cannot be started with P or N
        // (any other letter, or a search already running, is unaffected).
        if (keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift
            && keyEvent.ConsoleKey is ConsoleKey.P or ConsoleKey.N
            && ContentModel is not null && ContentModel.Filter is null)
        {
            var handled = keyEvent.ConsoleKey == ConsoleKey.P
                ? _terminalModel.RecallPrevious()
                : _terminalModel.RecallNext();
            if (handled)
            {
                FindContent()?.RevealCommandLine();
                return true;
            }
        }

        // ALT + printable character starts a new search (first key requires ALT).
        if (ContentModel is not null && ContentModel.Filter is null
            && keyEvent.Alt && IsFilterCharacter(keyEvent))
        {
            UpdateFilter(_ => string.Empty + keyEvent.KeyChar);
            return true;
        }

        // While a search is active subsequent printable keys extend the pattern (no ALT required).
        if (ContentModel?.Filter is not null && !keyEvent.Alt
            && IsFilterCharacter(keyEvent))
        {
            UpdateFilter(filter => (filter ?? string.Empty) + keyEvent.KeyChar);
            return true;
        }

        if (ContentModel?.Filter is not null && keyEvent.ConsoleKey == ConsoleKey.Backspace)
        {
            UpdateFilter(filter => string.IsNullOrEmpty(filter) ? null : filter[..^1]);
            return true;
        }

        if (ContentModel?.Filter is not null && keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            UpdateFilter(_ => null);
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private static bool IsFilterCharacter(KeyEvent keyEvent) =>
        keyEvent.ConsoleKey != 0
        && (keyEvent.ConsoleKey < ConsoleKey.F1 || keyEvent.ConsoleKey > ConsoleKey.F12)
        && !char.IsControl(keyEvent.KeyChar);

    private void UpdateFilter(Func<string?, string?> updateFilter)
    {
        ArgumentNullException.ThrowIfNull(ContentModel);

        var contentModel = ContentModel;
        var filterableContentModel = contentModel as ContentModelBase;

        if (filterableContentModel is not null)
        {
            filterableContentModel.RememberFocusedEntry();
            filterableContentModel.RememberFilterRestoreEntry();
        }

        contentModel.Filter = updateFilter(contentModel.Filter);

        if (filterableContentModel is not null)
        {
            if (contentModel.Filter is not null)
            {
                var firstMatchingIndex = filterableContentModel.GetFirstMatchingIndex();
                if (firstMatchingIndex >= 0)
                {
                    contentModel.CursorPosition.Y = firstMatchingIndex;
                    contentModel.ScrollToVisible();
                }
            }
            else
            {
                filterableContentModel.RestoreFocusedEntry();
            }
        }
        else if (contentModel.Filter is not null)
        {
            contentModel.ScrollPosition.Y = 0;
            contentModel.CursorPosition.Y = 0;
            contentModel.ScrollToVisible();
        }

        contentModel.Update();
        StateHasChanged();
    }

    public async Task ChangeModelAsync(string name)
    {
        // The old model outlives this browser's interest in it; left subscribed, every change to it
        // kept repainting this panel. OnInitializeAsync below subscribes to the new one.
        if (ContentModel is not null)
        {
            ContentModel.PropertyChanged -= OnContentModelChanged;
        }

        ContentModel = ContentModelService.ContentModels.SingleOrDefault(m => m.Name == name);
        if (ContentModel is ContentModelPreview)
        {
            // The Preview panel offers no listing mode; a panel that becomes Preview goes back to Full.
            ListingMode = PanelListingMode.Full;
        }

        ApplyListingLayout();
        Commands.Clear();
        await OnInitializeAsync();
    }
}
