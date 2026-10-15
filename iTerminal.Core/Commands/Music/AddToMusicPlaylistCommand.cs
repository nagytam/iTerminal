using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Music;

/// <summary>
/// F5 from the catalog panel to the playlist panel: adds the marked rows — or, when nothing is
/// marked, whatever the cursor is on — to the playing queue. Each row may be one track, a whole
/// album or an artist's entire catalogue. This is the point of running the library and the queue as
/// two panels rather than as two panes of a dialog; "copy from the left panel to the right one"
/// already means the right thing to anyone who uses this app, marks included.
///
/// <para>It only claims F5 when the <em>other</em> panel is actually showing the playlist. With any
/// other target the key falls through to the ordinary copy dialog, so copying indexed files out to
/// a real folder still works.</para>
/// </summary>
public class AddToMusicPlaylistCommand(
    IContentModel contentModel,
    IContentModelService contentModelService)
    : MusicPanelCommand(contentModel, contentModelService)
{
    public override string Title => "Add to playlist";
    public override string Description => "Copy the marked artists, albums or tracks — or the row under the cursor — from the catalog into the playlist.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F5, false, false, false);

    private ContentModelMusicCatalog? Catalog => FocusedMusicModel as ContentModelMusicCatalog;

    /// <summary>
    /// True only when the catalog has something to add — marked rows, or a row under the cursor —
    /// and the opposite panel holds the playlist.
    /// </summary>
    public override bool CanExecute() =>
        Catalog is not null
        && (Catalog.SelectionCount > 0 || FocusedMusicEntry is not null)
        && ContentModelService.ContentModels.Any(model =>
            model is ContentModelMusicPlaylist && !ReferenceEquals(model, ContentModel));

    /// <summary>
    /// Marked rows win over the cursor. That is the file-manager rule — once something is marked,
    /// the cursor is just where you happen to be standing — and it is the only reading under which
    /// marking a row and pressing F5 without moving does not silently add the wrong thing twice.
    /// </summary>
    public override Task ExecuteAsync(object? args)
    {
        var catalog = Catalog;
        if (catalog is null)
        {
            return Task.CompletedTask;
        }

        var added = catalog.SelectionCount > 0
            ? catalog.AddSelectionToPlaylist()
            : catalog.AddToPlaylist(FocusedMusicEntry);

        if (added > 0)
        {
            // The playlist model rebuilds itself from the player's StateChanged, but the panel only
            // repaints when its own model says so, so nudge the target explicitly.
            foreach (var model in ContentModelService.ContentModels.OfType<ContentModelMusicPlaylist>())
            {
                model.Refresh();
            }
        }

        return Task.CompletedTask;
    }
}
