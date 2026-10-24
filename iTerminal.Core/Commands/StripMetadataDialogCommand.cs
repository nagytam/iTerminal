using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Strip Metadata: removes the camera, GPS, author and comment text from the marked JPEG and PNG files, or the one under the
/// cursor (<see cref="StripMetadataDialog"/>). Local disk only: the files are rewritten in place.
/// </summary>
public class StripMetadataDialogCommand(
    StripMetadataDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<StripMetadataDialog>(dialog)
{
    public override string Title => "Strip Metadata";
    public override string Description => "Remove the camera, GPS, author and comment text from the marked JPEG and PNG files (or the one under the cursor). The files are overwritten.";

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

    protected override void OnBeforeShow(StripMetadataDialog dialog)
    {
        // Read now, while the panel still has focus: the dialog takes it as it opens.
        dialog.Load(Targets.Select(url => url.FullName));
    }
}
