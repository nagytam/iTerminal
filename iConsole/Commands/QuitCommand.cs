using iConsole.Data;
using iConsole.Screens;

namespace iConsole.Commands;

public class QuitCommand : Command
{
    public override string Title => "Quit";
    public override string Description => "Exit the application.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F10, false, false, false);

    /// <summary>
    /// Ctrl+C, accepted alongside <see cref="Key"/> as the terminal-convention alternate for
    /// quitting (nano, htop, etc). Safe on both platforms: POSIX raw mode clears ISIG so the
    /// kernel never turns it into SIGINT, and Windows raw mode clears ENABLE_PROCESSED_INPUT so
    /// it isn't auto-handled as CTRL_C_EVENT — either way it reaches this command as a plain key.
    /// </summary>
    public Key AlternateKey { get; init; } = new(ConsoleKey.C, false, false, true);

    public override string KeyDisplayText => $"{FormatKey(ActiveKey)} / {FormatKey(AlternateKey)}";

    public override Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // F10 and Ctrl+F10 both quit; Shift+F10 is left alone. Match on the key, ignoring Alt: Windows'
        // raw console input can report a stale ALT_PRESSED bit on the very next key event after an ALT-chord used elsewhere (e.g.
        // the file panel's Alt+letter search filter), which would otherwise make the strict
        // Key.IsIdentical comparison silently fail to quit. F10 has no other binding and
        // Alt+F10 isn't bound to anything, so relaxing Alt here is safe.
        if (IsActiveKey(keyEvent) || AlternateKey.IsIdentical(keyEvent))
        {
            return RunAsync(null);
        }
        return Task.FromResult(false);
    }

    /// <summary>
    /// Whether the key is the one this command answers to now: the user's mapped key, or F10 with Ctrl or Alt
    /// tolerated as above. A mapped key of <c>none</c> leaves only <see cref="AlternateKey"/>.
    /// </summary>
    private bool IsActiveKey(KeyEvent keyEvent)
    {
        if (ActiveKey is not { } active || active.ConsoleKey != keyEvent.ConsoleKey)
        {
            return false;
        }

        return active.ConsoleKey == ConsoleKey.F10
            ? keyEvent.Shift == active.Shift
            : active.IsIdentical(keyEvent);
    }

    public override Task ExecuteAsync(object? args)
    {
        var screen = (Control?.GetRootControl() as Screen);
        var desktop = screen?.Desktop;
        desktop?.CancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }
}
