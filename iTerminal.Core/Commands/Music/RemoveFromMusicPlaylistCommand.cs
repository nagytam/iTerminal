using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Music;

/// <summary>
/// Delete drops the row under the cursor out of the queue. Nothing on disk is touched — the
/// playlist is a list of references — which is exactly why this is Delete rather than F8: F8 opens
/// the delete dialog, and that dialog's promise ("this removes the file") is not the promise being
/// made here.
/// </summary>
public class RemoveFromMusicPlaylistCommand(
    IContentModel contentModel,
    IContentModelService contentModelService)
    : MusicPanelCommand(contentModel, contentModelService)
{
    public override string Title => "Remove from playlist";
    public override string Description => "Take the selected track out of the playing queue. The file is left alone.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Delete, false, false, false);

    private ContentModelMusicPlaylist? Playlist => FocusedMusicModel as ContentModelMusicPlaylist;

    public override bool CanExecute() =>
        Playlist is not null && FocusedMusicEntry is { Kind: MusicEntryKind.Track };

    public override Task ExecuteAsync(object? args)
    {
        Playlist?.RemoveFromPlaylist(FocusedMusicEntry);
        return Task.CompletedTask;
    }
}
