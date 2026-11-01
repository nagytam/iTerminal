using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>Move dialog. All behavior lives in <see cref="TransferDialog"/>.</summary>
public class MoveDialog : TransferDialog
{
    public MoveDialog(
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
        IArchiveFileSystemService archiveFileSystemService)
        : base(
            nameof(MoveDialog),
            "Move",
            new MoveCommand(taskManager, progressWindowFactory, errorPromptService, conflictPromptService,
                new FileSystemResolver(fileSystemService, memoryDriveService, sshFileSystemService, archiveFileSystemService, contentModelService),
                contentModelService),
            'M',
            contentModelService,
            beepService,
            dateTimeProviderService,
            themeService)
    {
    }
}
