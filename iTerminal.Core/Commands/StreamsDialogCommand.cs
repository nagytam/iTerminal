using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Lists, previews and deletes the NTFS alternate data streams (Windows) or extended attributes (Linux, macOS) of the
/// file or folder under the cursor (<see cref="StreamsDialog"/>). Local disk only — the calls are handed a path.
/// It has no chord: it is a rarely wanted inspection, reachable from the File menu, the right-click menu and the command
/// palette, and every free chord is being claimed by something else.
/// </summary>
public class StreamsDialogCommand(
    StreamsDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<StreamsDialog>(dialog)
{
    public override string Title => "Streams and Attributes";
    public override string Description => "List, preview and delete the alternate data streams (Windows) or extended attributes (Linux, macOS) of the entry under the cursor.";

    /// <summary>The panel the command acts on: the one the menu was opened over, when it runs from the menu or a dialog.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    /// <summary>The local file or folder under the cursor; never the <c>..</c> row, a container row or another drive.</summary>
    private Url? Target =>
        FocusedEntry.ActionableUrl(SourceModel) is { ContainerType: ContainerType.FileSystem } url ? url : null;

    public override bool CanExecute() => Target is not null;

    protected override void OnBeforeShow(StreamsDialog dialog)
    {
        // Read now, while the panel still has focus: the dialog takes it as it opens.
        if (Target is { } url)
        {
            dialog.Load(url.FullName);
        }
    }
}
