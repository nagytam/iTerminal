using iConsole.Commands;
using iConsole.Controls;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.Dotnet;

namespace iTerminal.Core.Controls.MenuBar;

public class FileMenuItem : MenuItem
{
    public FileMenuItem(
        IContentModelService contentModelService,
        FileViewerDialog fileViewerDialog,
        TextFileEditorDialog textFileEditorDialog,
        CreateFolderDialog createFolderDialog,
        CreateFileDialog createFileDialog,
        RenameDialog renameDialog,
        IThemeService themeService,
        SolutionViewerDialog? solutionViewerDialog = null,
        iTerminal.Core.Dialogs.Music.MusicFileViewerDialog? musicFileViewerDialog = null,
        iTerminal.Core.Dialogs.Video.VideoFileViewerDialog? videoFileViewerDialog = null,
        iTerminal.Core.Dialogs.Video.ImageFileViewerDialog? imageFileViewerDialog = null,
        iTerminal.Core.Dialogs.Search.FindFilesDialog? findFilesDialog = null,
        CompressDialog? compressDialog = null,
        ExtractDialog? extractDialog = null,
        iTerminal.Core.Services.FileAssociations.FileAssociationRunner? fileAssociations = null,
        Func<OpenWithDialog>? openWithDialogFactory = null,
        CreateLinkDialog? createLinkDialog = null,
        PropertiesDialog? propertiesDialog = null,
        ChecksumDialog? checksumDialog = null,
        iTerminal.Core.Services.FileSystemResolver? fileSystemResolver = null,
        SecureWipeDialog? secureWipeDialog = null,
        SignatureDialog? signatureDialog = null,
        iTerminal.Core.Services.Defender.IMalwareScanService? malwareScanService = null,
        INotificationService? notificationService = null)
    : base("File", null, [], themeService)
    {
        HotKeyChar = 'F';

        AddMenuItem(new MenuItem("View\tF3",   new ViewFileDialogCommand(fileViewerDialog, contentModelService, solutionViewerDialog, musicFileViewerDialog, videoFileViewerDialog, imageFileViewerDialog), [], themeService));
        AddMenuItem(new MenuItem("Quick View\tCtrl+F3", new iTerminal.Core.Commands.Drives.TogglePreviewCommand(contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Edit\tF4",   new EditTextFileDialogCommand(textFileEditorDialog, contentModelService, fileAssociations), [], themeService));
        if (openWithDialogFactory is not null)
        {
            AddMenuItem(new MenuItem("Open With…\tShift+F3", new OpenWithDialogCommand(openWithDialogFactory(), contentModelService, iTerminal.Core.Services.FileAssociations.FileAssociationKind.Open), [], themeService));
            AddMenuItem(new MenuItem("Edit With…\tCtrl+F4", new OpenWithDialogCommand(openWithDialogFactory(), contentModelService, iTerminal.Core.Services.FileAssociations.FileAssociationKind.Edit, textFileEditorDialog), [], themeService));
        }
        AddMenuItem(new MenuItem("Find Files\tF7", new FindFilesDialogCommand(findFilesDialog!), [], themeService));
        AddMenuItem(new MenuItem("Find Large Files", new FindLargeFilesCommand(findFilesDialog!), [], themeService));
        AddMenuItem(new MenuItem("Find Old Files", new FindOldFilesCommand(findFilesDialog!), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Create Folder\tShift+F7", new CreateFolderDialogCommand(createFolderDialog, contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Create File\tShift+F4", new CreateFileDialogCommand(createFileDialog, contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Rename\tF6", new RenameDialogCommand(renameDialog, contentModelService), [], themeService));
        if (createLinkDialog is not null)
        {
            AddMenuItem(new MenuItem("Create Link\tCtrl+L", new CreateLinkDialogCommand(createLinkDialog, contentModelService), [], themeService));
        }
        if (compressDialog is not null)
        {
            AddMenuItem(new MenuItem("Compress\tAlt+F5", new CompressDialogCommand(compressDialog, contentModelService), [], themeService));
        }
        if (extractDialog is not null)
        {
            AddMenuItem(new MenuItem("Extract\tShift+F5", new ExtractDialogCommand(extractDialog, contentModelService), [], themeService));
        }
        if (checksumDialog is not null)
        {
            AddMenuItem(new MenuItem("Checksums\tShift+F12", new ChecksumDialogCommand(checksumDialog, contentModelService, fileSystemResolver), [], themeService));
        }
        if (signatureDialog is not null)
        {
            AddMenuItem(new MenuItem("Signatures	Ctrl+Shift+F7", new SignatureDialogCommand(signatureDialog, contentModelService), [], themeService));
        }
        if (secureWipeDialog is not null)
        {
            AddMenuItem(new MenuItem("Secure Wipe	Ctrl+Shift+F8", new SecureWipeDialogCommand(secureWipeDialog, contentModelService), [], themeService));
        }
        if (malwareScanService is { IsAvailable: true })
        {
            AddMenuItem(new MenuItem("Scan with Microsoft Defender", new ScanWithDefenderCommand(contentModelService, malwareScanService, notificationService), [], themeService));
        }
        if (propertiesDialog is not null)
        {
            AddMenuItem(new MenuItem("Properties\tCtrl+F12", new PropertiesDialogCommand(propertiesDialog, contentModelService), [], themeService));
        }
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Quit\tF10",  new QuitCommand(), [], themeService));
    }
}

