using iConsole.Localization;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Copies a single file between two <see cref="IFileSystem"/> implementations. See
/// <see cref="ContentModelFileTransferTask"/> for the shared streaming engine.
/// </summary>
public class ContentModelCopyFileTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url targetUrl, bool overwrite, IErrorPromptService? errorPromptService = null)
    : ContentModelFileTransferTask(source, target, sourceUrl, targetUrl, overwrite, errorPromptService)
{
    protected override string Verb => UiText.Get("Verb.Copy", "Copy");
    protected override string ProgressVerb => UiText.Get("Verb.Copying", "Copying");
    protected override bool DeletesSource => false;

    protected override Task SameFileSystemTransferAsync(IFileSystem fileSystem, Url sourceUrl, Url targetUrl, bool overwrite) =>
        fileSystem.CopyAsync(sourceUrl, targetUrl, overwrite);
}
