using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class AiModelDialogCommand(AiModelDialog dialog) : ShowDialogCommand<AiModelDialog>(dialog)
{
    public override string Title => "AI model info";
    public override string Description => "Show AI model configuration, status, and session details.";

    // Ctrl+F2, beside F2 (the terminal) — the other half of the bar below the panels. It used to be
    // Alt+I, which reached the command only while the AI bar itself had focus: from a file panel the
    // panel's type-to-search took it as "Filter: i", although F1 listed it there.
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F2, false, false, true);
}
