using iConsole.Localization;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services.Checksums;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Recursively copies a folder between two <see cref="IFileSystem"/> implementations. See
/// <see cref="ContentModelFolderTransferTask"/> for the shared discovery/copy pipeline.
/// </summary>
public class ContentModelCopyFolderTask(
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
    protected override string Verb => UiText.Get("Verb.Copy", "Copy");
    protected override string ProgressVerb => UiText.Get("Verb.Copying", "Copying");
    protected override bool DeletesSource => false;

    protected override Task SameFileSystemTransferAsync(IFileSystem fileSystem, Url sourceUrl, Url targetUrl, bool overwrite) =>
        fileSystem.CopyAsync(sourceUrl, targetUrl, overwrite);
}
