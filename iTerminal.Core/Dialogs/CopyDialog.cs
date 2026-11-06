using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;

namespace iTerminal.Core.Dialogs;

/// <summary>Copy dialog. All behavior lives in <see cref="TransferDialog"/>.</summary>
public class CopyDialog : TransferDialog
{
    public CopyDialog(
        TaskManager taskManager,
        IContentModelService contentModelService,
        ProgressWindowFactory progressWindowFactory,
        IErrorPromptService errorPromptService,
        IConflictPromptService conflictPromptService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        IFileSystemService fileSystemService,
        IMemoryDriveService memoryDriveService,
        ISshFileSystemService sshFileSystemService,
        IArchiveFileSystemService archiveFileSystemService,
        ISettingsService? settingsService = null,
        IChecksumService? checksumService = null)
        : base(
            nameof(CopyDialog),
            "Copy",
            new CopyCommand(taskManager, progressWindowFactory, errorPromptService, conflictPromptService,
                new FileSystemResolver(fileSystemService, memoryDriveService, sshFileSystemService, archiveFileSystemService, contentModelService),
                contentModelService, checksumService),
            'y',
            contentModelService,
            beepService,
            dateTimeProviderService,
            themeService,
            settingsService)
    {
    }
}
