using iConsole.Localization;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services.Checksums;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Moves a folder between two <see cref="IFileSystem"/> implementations. See
/// <see cref="ContentModelFolderTransferTask"/> for the shared discovery/copy pipeline and the
/// source-removal semantics on partial failure.
/// </summary>
public class ContentModelMoveFolderTask(
    IFileSystem source,
    IFileSystem target,
    Url sourceUrl,
    Url targetUrl,
    bool overwrite,
    FolderDiscoveryTask? discoveryTask,
    IErrorPromptService errorPromptService,
    TransferVerifier? verifier = null)
    : ContentModelFolderTransferTask(source, target, sourceUrl, targetUrl, overwrite, discoveryTask, errorPromptService, verifier)
{
    protected override string Verb => UiText.Get("Verb.Move", "Move");
    protected override string ProgressVerb => UiText.Get("Verb.Moving", "Moving");
    protected override bool DeletesSource => true;

    protected override Task SameFileSystemTransferAsync(IFileSystem fileSystem, Url sourceUrl, Url targetUrl, bool overwrite) =>
        fileSystem.MoveAsync(sourceUrl, targetUrl, overwrite);
}
