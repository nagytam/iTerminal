using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+F8: overwrite and delete the marked files and folders, or the one under the cursor
/// (<see cref="SecureWipeDialog"/>). Local disk only — an overwrite is a promise about one file
/// system's sectors.
/// </summary>
public class SecureWipeDialogCommand(
    SecureWipeDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<SecureWipeDialog>(dialog)
{
    public override string Title => "Secure Wipe";
    public override string Description => "Overwrite the marked files and folders (or the one under the cursor) and delete them so they cannot be recovered.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F8, true, false, true);

    /// <summary>The panel the key acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    /// <summary>Every local file or folder the key acts on; anything on another drive makes the whole selection refused rather than half wiped.</summary>
    private IReadOnlyList<Url> Targets
    {
        get
        {
            var urls = FocusedEntry.ActionableUrls(SourceModel);
            return urls.Count > 0 && urls.All(url => url.ContainerType == ContainerType.FileSystem) ? urls : [];
        }
    }

    public override bool CanExecute() => Targets.Count > 0;

    protected override void OnBeforeShow(SecureWipeDialog dialog)
    {
        // Read now, while the panel still has focus: the dialog takes it as it opens.
        dialog.Load(Targets.Select(url => url.FullName));
    }
}
