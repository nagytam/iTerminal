using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Commands.Music;

/// <summary>
/// Puts the music player on screen: the indexed catalog in the left panel, the playing queue in the
/// right one. It replaces the old modal player, and the arrangement is the feature — the catalog and
/// the queue are two drives, so adding an album to the playlist is the same F5 that copies a file
/// between any other two panels.
///
/// <para>Both chords are kept from the dialog it replaces, and for the same reasons: Ctrl+M is
/// ASCII CR — indistinguishable from Enter on a Linux TTY — and hosts commonly reserve
/// Ctrl+M / Ctrl+Shift+M, so a function key is the binding that actually arrives. Alt+M is not
/// available at all: the focused file panel claims Alt+printable for its type-to-search filter
/// before the status bar ever sees the key (see <c>ContentBrowser.HandleKeyEventAsync</c>).</para>
/// </summary>
public class MusicPanelsCommand(IContentModelService? contentModelService) : Command
{
    private readonly IContentModelService _contentModelService =
        contentModelService ?? iTerminal.Core.Services.NullContentModelService.Instance;

    public override string Title => "Music player";
    public override string Description => "Show the music catalog and the playlist as the two file panels.";

    // Key ctor is (consoleKey, shift, alt, control) — shift comes BEFORE control — so this is
    // Ctrl+Shift+M.
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.M, true, false, true);

    /// <summary>Ctrl+F7, accepted alongside <see cref="Key"/> because function keys pass every layer.</summary>
    public Key AlternateKey { get; init; } = new(ConsoleKey.F7, false, false, true);

    public override string KeyDisplayText => $"{FormatKey(Key)} / {FormatKey(AlternateKey)}";

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (AlternateKey.IsIdentical(keyEvent))
        {
            return await RunAsync(null);
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task ExecuteAsync(object? args)
    {
        await _contentModelService.SetUrlAsync(Content.Left, new Url(Container.MUSIC_CATALOG));
        await _contentModelService.SetUrlAsync(Content.Right, new Url(Container.MUSIC_PLAYLIST));
    }
}
