using iConsole.Commands;
using iConsole.Data;

namespace iTerminal.Core.Commands;

/// <summary>
/// Starts and ends a voice recording for the AI bar's question. It acts only while the bar can take a question and
/// has the focus (<paramref name="canRun"/>), so the key does nothing from a panel or while the assistant is unavailable.
/// </summary>
public class VoiceInputCommand(Func<bool> canRun, Action toggle) : Command
{
    public override string Title => "Voice input";

    public override string Description => "Record a spoken question and type it into the AI prompt (offline Whisper).";

    // Alt+F1: every other F1 chord is taken, and Alt+letter belongs to the panels' type-to-search.
    public override Key? Key { get; init; } = new Key(ConsoleKey.F1, false, true, false);

    public override bool CanExecute() => canRun();

    public override Task ExecuteAsync(object? args)
    {
        toggle();
        return Task.CompletedTask;
    }
}
