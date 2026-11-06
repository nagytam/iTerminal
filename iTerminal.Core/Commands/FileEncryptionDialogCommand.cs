using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+E: encrypt the marked files (or the one under the cursor) with a passphrase, or decrypt <c>.enc</c> files
/// (<see cref="FileEncryptionDialog"/>). Local disk only: the work streams through local file handles.
/// </summary>
public class FileEncryptionDialogCommand(
    FileEncryptionDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<FileEncryptionDialog>(dialog)
{
    public override string Title => "Encrypt / Decrypt Files";
    public override string Description => "Encrypt the marked files (or the one under the cursor) with a passphrase into separate .enc files, or decrypt .enc files.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.E, true, false, true);

    /// <summary>The panel the key acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    /// <summary>Every local file the key acts on; a selection that reaches another drive is refused whole.</summary>
    private IReadOnlyList<Url> Targets
    {
        get
        {
            var urls = FocusedEntry.ActionableUrls(SourceModel);
            return urls.Count > 0 && urls.All(url => url.ContainerType == ContainerType.FileSystem) ? urls : [];
        }
    }

    public override bool CanExecute() => Targets.Count > 0;

    protected override void OnBeforeShow(FileEncryptionDialog dialog)
    {
        // Read now, while the panel still has focus: the dialog takes it as it opens.
        dialog.Load(Targets.Select(url => url.FullName));
    }
}
