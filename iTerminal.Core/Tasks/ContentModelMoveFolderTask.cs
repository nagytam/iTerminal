using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;

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
    IErrorPromptService errorPromptService)
    : ContentModelFolderTransferTask(source, target, sourceUrl, targetUrl, overwrite, discoveryTask, errorPromptService)
{
    protected override string Verb => "Move";
    protected override string ProgressVerb => "Moving";
    protected override bool DeletesSource => true;

    protected override Task SameFileSystemTransferAsync(IFileSystem fileSystem, Url sourceUrl, Url targetUrl, bool overwrite) =>
        fileSystem.MoveAsync(sourceUrl, targetUrl, overwrite);
}
