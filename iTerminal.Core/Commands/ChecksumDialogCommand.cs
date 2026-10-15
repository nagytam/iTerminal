using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+F12: the checksums of the file under the cursor (<see cref="ChecksumDialog"/>) — on any
/// drive a file can be read from: the file system that serves it is resolved here, while the panel
/// still has focus, and handed to the dialog.
/// </summary>
public class ChecksumDialogCommand(
    ChecksumDialog dialog,
    IContentModelService contentModelService,
    FileSystemResolver? fileSystemResolver = null)
    : ShowDialogCommand<ChecksumDialog>(dialog)
{
    public override string Title => "Checksums";
    public override string Description => "Compute the selected file's MD5, SHA-1, SHA-256 and SHA-512, and compare them with a checksum.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F12, true, false, false);

    /// <summary>The panel the key acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    private Url? SourceUrl =>
        FocusedEntry.ActionableUrl(SourceModel) is { PathType: PathType.File } url ? url : null;

    public override bool CanExecute() => SourceUrl is not null;

    protected override void OnBeforeShow(ChecksumDialog dialog)
    {
        var model = SourceModel;
        if (SourceUrl is not { } url)
        {
            return;
        }

        IFileSystem? fileSystem = null;
        if (url.ContainerType != ContainerType.FileSystem)
        {
            try
            {
                fileSystem = fileSystemResolver?.Resolve(url, model, operation: "Checksum");
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
            {
                // The dialog says so itself, when the read fails for want of a file system.
            }
        }

        dialog.Load(url, fileSystem);
    }
}
