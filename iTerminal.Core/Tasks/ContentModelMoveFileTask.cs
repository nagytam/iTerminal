using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Moves a single file between two <see cref="IFileSystem"/> implementations: the shared engine
/// (<see cref="ContentModelFileTransferTask"/>) streams the content across and then deletes the
/// source, or delegates to <see cref="IFileSystem.MoveAsync"/> when both ends are the same drive.
/// </summary>
public class ContentModelMoveFileTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url targetUrl, bool overwrite, IErrorPromptService? errorPromptService = null)
    : ContentModelFileTransferTask(source, target, sourceUrl, targetUrl, overwrite, errorPromptService)
{
    protected override string Verb => "Move";
    protected override string ProgressVerb => "Moving";
    protected override bool DeletesSource => true;

    protected override Task SameFileSystemTransferAsync(IFileSystem fileSystem, Url sourceUrl, Url targetUrl, bool overwrite) =>
        fileSystem.MoveAsync(sourceUrl, targetUrl, overwrite);
}
