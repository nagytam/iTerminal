using iConsole.Localization;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Commands;

/// <summary>Runs the Copy dialog's primary action. See <see cref="TransferCommand"/>.</summary>
public class CopyCommand(
    TaskManager taskManager,
    ProgressWindowFactory progressWindowFactory,
    IErrorPromptService errorPromptService,
    IConflictPromptService conflictPromptService,
    FileSystemResolver fileSystemResolver,
    IContentModelService contentModelService,
    IChecksumService? checksumService = null)
    : TransferCommand(taskManager, progressWindowFactory, errorPromptService, conflictPromptService, fileSystemResolver, contentModelService, checksumService)
{
    protected override string OperationName => UiText.Get("Verb.Copy", "Copy");

    protected override bool RemovesSource => false;

    protected override BackgroundTask CreateFileTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url targetUrl, FileConflictResolver resolver, TransferVerifier? verifier) =>
        new ContentModelCopyFileTask(source, target, sourceUrl, targetUrl, resolver, ErrorPromptService, verifier);

    protected override TransferTask CreateFolderTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url targetUrl, bool overwrite, FolderDiscoveryTask? discoveryTask, TransferVerifier? verifier) =>
        new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite, discoveryTask, ErrorPromptService, verifier);
}
