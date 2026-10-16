using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+F7: the signatures of the file under the cursor, and signing it
/// (<see cref="SignatureDialog"/>). Local files only: gpg and PowerShell are handed a path.
/// </summary>
public class SignatureDialogCommand(
    SignatureDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<SignatureDialog>(dialog)
{
    public override string Title => "Signatures";
    public override string Description => "Check the signatures of the file under the cursor (OpenPGP, Authenticode, key file) and sign it.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F7, true, false, true);

    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    private Url? SourceUrl =>
        FocusedEntry.ActionableUrl(SourceModel) is { PathType: PathType.File, ContainerType: ContainerType.FileSystem } url ? url : null;

    public override bool CanExecute() => SourceUrl is not null;

    protected override void OnBeforeShow(SignatureDialog dialog)
    {
        if (SourceUrl is { } url)
        {
            dialog.Load(url.FullName);
        }
    }
}
