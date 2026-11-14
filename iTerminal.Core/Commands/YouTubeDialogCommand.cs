using iConsole.Data;
using iConsole.Localization;
using iTerminal.Core.Dialogs.YouTube;

namespace iTerminal.Core.Commands;

public class YouTubeDialogCommand(YouTubeDialog youTubeDialog) : ShowDialogCommand<YouTubeDialog>(youTubeDialog)
{
    public override string Title => "YouTube";
    public override string Description => UiText.Describe("Search YouTube and play a video in the video viewer.");

    // No global hot key: the function keys and Ctrl+Shift chords are taken, and an Alt+letter one would clash with type-to-search.
    public override Key? Key { get; init; } = null;
}
