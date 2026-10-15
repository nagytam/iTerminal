using iConsole.Commands;
using iConsole.Data;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Commands;

public class ToggleTerminalCommand(Content? content) : Command
{
    public override string Title => "Full-screen command log";
    public override string Description => "Show or hide the terminal panel full screen: commands run one at a time, with their output kept.";

    // Ctrl+Shift+O: Ctrl+O is the interactive shell (InteractiveShellCommand) since there is one.
    // No Alt+letter: the focused file panel claims Alt+printable for its type-to-search, and the
    // Key constructor takes (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.O, true, false, true);

    public override Task ExecuteAsync(object? arg)
    {
        content?.ToggleTerminalPanel();
        return Task.CompletedTask;
    }
}
