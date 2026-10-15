using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Music;

/// <summary>
/// Insert on a catalog row: marks or unmarks it and steps down, the way every file manager marks
/// files. Marking is what turns "add this album" into "add these six albums" without six trips to
/// the other panel — <see cref="AddToMusicPlaylistCommand"/> adds the marked rows when there are
/// any.
///
/// <para>Only the catalog has marks. The playlist is a queue that is already the selection, so
/// marking rows in it would mean nothing that <c>Delete</c> does not already do.</para>
/// </summary>
public class ToggleMusicSelectionCommand(
    IContentModel contentModel,
    IContentModelService contentModelService)
    : MusicPanelCommand(contentModel, contentModelService)
{
    public override string Title => "Mark";
    public override string Description => "Mark or unmark the row under the cursor and step down.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Insert, false, false, false);

    private ContentModelMusicCatalog? Catalog => FocusedMusicModel as ContentModelMusicCatalog;

    public override bool CanExecute() => Catalog is not null;

    public override Task ExecuteAsync(object? args)
    {
        Catalog?.ToggleSelectionAtCursor();
        return Task.CompletedTask;
    }
}
