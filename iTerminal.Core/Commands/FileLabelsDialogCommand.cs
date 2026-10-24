using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Colour Labels and Tags: sets the colour and the tags of the marked files and folders, or the one under the cursor
/// (<see cref="FileLabelsDialog"/>). Local disk only: the labels are kept by path.
/// </summary>
public class FileLabelsDialogCommand(
    FileLabelsDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<FileLabelsDialog>(dialog)
{
    public override string Title => "Colour Labels and Tags";
    public override string Description => "Give the marked files and folders (or the one under the cursor) a colour and tags.";

    /// <summary>The panel the command acts on: the one focused, or the one the menu was opened over.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    /// <summary>Every local file or folder the command acts on; anything on another drive makes the whole selection refused.</summary>
    private IReadOnlyList<Url> Targets
    {
        get
        {
            var urls = FocusedEntry.ActionableUrls(SourceModel);
            return urls.Count > 0 && urls.All(url => url.ContainerType == ContainerType.FileSystem) ? urls : [];
        }
    }

    public override bool CanExecute() => Targets.Count > 0;

    protected override void OnBeforeShow(FileLabelsDialog dialog)
    {
        // Read now, while the panel still has focus: the dialog takes it as it opens.
        dialog.Load(Targets.Select(url => url.FullName).ToList());
    }
}
