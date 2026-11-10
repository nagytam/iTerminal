using iConsole.Data;
using iTerminal.Core.AI.Voice;
using iTerminal.Core.Controls.Terminals;

namespace iTerminal.Core.Commands;

/// <summary>Switches dictation into the AI bar on or off.</summary>
public class ToggleVoiceInputCommand(AiPromptBar bar) : iConsole.Commands.Command
{
    public override string Title => "Voice input";
    public override string Description => "Start or stop dictating into the AI prompt (microphone).";

    // Ctrl+F7: a function key, as global hot keys are, and the free one beside F7 (find) in the Ctrl row.
    public override Key? Key { get; init; } = new Key(ConsoleKey.F7, false, false, true);

    public override Task ExecuteAsync(object? args)
    {
        bar.ToggleVoiceInput();
        return Task.CompletedTask;
    }
}
