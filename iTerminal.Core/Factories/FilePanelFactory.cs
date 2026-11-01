using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Controls.Lenses;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Serilog;

namespace iTerminal.Core.Factories;

public class FilePanelFactory(
    TaskManager taskManager,
    IContentModelService contentModelService,
    TerminalModel terminalModel,
    IMemoryCacheService memoryCacheService,
    ISettingsService settingsService,
    ILogger logger,
    IThemeService themeService,
    CreateFolderDialog? createFolderDialog = null,
    CopyDialog? copyDialog = null,
    DeleteDialog? deleteDialog = null,
    MoveDialog? moveDialog = null,
    FileViewerDialog? fileViewerDialog = null,
    TextFileEditorDialog? textFileEditorDialog = null,
    HelpDialog? helpDialog = null,
    iTerminal.Core.Dialogs.Dotnet.SolutionViewerDialog? solutionViewerDialog = null,
    IEnumerable<IContentLens>? contentLenses = null,
    iTerminal.Core.Dialogs.Music.MusicFileViewerDialog? musicFileViewerDialog = null,
    iTerminal.Core.Dialogs.Video.VideoFileViewerDialog? videoFileViewerDialog = null,
    iTerminal.Core.Dialogs.Video.ImageFileViewerDialog? imageFileViewerDialog = null,
    XmlViewerDialog? xmlViewerDialog = null,
    JsonViewerDialog? jsonViewerDialog = null,
    CsvViewerDialog? csvViewerDialog = null,
    MarkPatternDialog? markPatternDialog = null,
    MarkPatternDialog? unmarkPatternDialog = null,
    CompressDialog? compressDialog = null,
    ExtractDialog? extractDialog = null,
    Services.FileAssociations.FileAssociationRunner? fileAssociations = null,
    Func<OpenWithDialog>? openWithDialogFactory = null,
    CreateLinkDialog? createLinkDialog = null,
    PropertiesDialog? propertiesDialog = null,
    ChecksumDialog? checksumDialog = null,
    FileSystemResolver? fileSystemResolver = null,
    SortDialog? sortDialog = null,
    UserMenuDialog? userMenuDialog = null,
    FileDiffDialog? fileDiffDialog = null,
    Services.Clipboard.FileClipboardOperations? clipboardOperations = null,
    DiskImageDialog? diskImageDialog = null,
    SecureWipeDialog? secureWipeDialog = null,
    SignatureDialog? signatureDialog = null,
    iTerminal.Core.Services.Defender.IMalwareScanService? malwareScanService = null,
    INotificationService? notificationService = null,
    EditLinkDialog? editLinkDialog = null,
    iTerminal.Core.Services.Git.IGitService? gitService = null)
{
    private TaskManager _taskManager { get; set; } = taskManager;

    public FilePanel Create(string name)
    {
        var contentBrowser = new ContentBrowser(
            name,
            contentModelService,
            terminalModel,
            _taskManager,
            themeService,
            createFolderDialog: createFolderDialog,
            copyDialog: copyDialog,
            deleteDialog: deleteDialog,
            moveDialog: moveDialog,
            textFileEditorDialog: textFileEditorDialog,
            helpDialog: helpDialog,
            fileViewerDialog: fileViewerDialog,
            solutionViewerDialog: solutionViewerDialog,
            contentLenses: contentLenses,
            musicFileViewerDialog: musicFileViewerDialog,
            videoFileViewerDialog: videoFileViewerDialog,
            imageFileViewerDialog: imageFileViewerDialog,
            xmlViewerDialog: xmlViewerDialog,
            jsonViewerDialog: jsonViewerDialog,
            csvViewerDialog: csvViewerDialog,
            markPatternDialog: markPatternDialog,
            unmarkPatternDialog: unmarkPatternDialog,
            compressDialog: compressDialog,
            extractDialog: extractDialog,
            fileAssociations: fileAssociations,
            // One each: a dialog instance is one control, and "Open with" and "Edit with" are two.
            openWithDialog: openWithDialogFactory?.Invoke(),
            editWithDialog: openWithDialogFactory?.Invoke(),
            createLinkDialog: createLinkDialog,
            propertiesDialog: propertiesDialog,
            checksumDialog: checksumDialog,
            fileSystemResolver: fileSystemResolver,
            sortDialog: sortDialog,
            userMenuDialog: userMenuDialog,
            fileDiffDialog: fileDiffDialog,
            clipboardOperations: clipboardOperations,
            diskImageDialog: diskImageDialog,
            secureWipeDialog: secureWipeDialog,
            signatureDialog: signatureDialog,
            malwareScanService: malwareScanService,
            notificationService: notificationService,
            editLinkDialog: editLinkDialog,
            gitService: gitService);
        contentModelService.AddContentBrowser(name, contentBrowser);
        return new FilePanel(
            name,
            contentBrowser,
            new ItemInformation(contentModelService, _taskManager, memoryCacheService, themeService, logger),
            contentModelService,
            settingsService,
            themeService);
    }
}
