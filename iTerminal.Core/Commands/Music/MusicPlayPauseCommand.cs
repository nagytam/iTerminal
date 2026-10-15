using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Music;

/// <summary>
/// Space starts or pauses playback from either music panel. The transport folder covers the same
/// ground with the cursor, but pause is the one control that has to be one keystroke away no matter
/// where in the drive you happen to be standing.
/// </summary>
public class MusicPlayPauseCommand(
    IContentModel contentModel,
    IContentModelService contentModelService)
    : MusicPanelCommand(contentModel, contentModelService)
{
    public override string Title => "Play or pause";
    public override string Description => "Start or pause playback without leaving the current folder.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Spacebar, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        FocusedMusicModel?.Perform(MusicAction.PlayPause);
        return Task.CompletedTask;
    }
}
