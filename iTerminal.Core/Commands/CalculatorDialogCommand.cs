using iConsole.Data;
using iTerminal.Core.Dialogs.Calculator;

namespace iTerminal.Core.Commands;

public class CalculatorDialogCommand(CalculatorDialog calculatorDialog) : ShowDialogCommand<CalculatorDialog>(calculatorDialog)
{
    public override string Title => "Calculator";
    public override string Description => "Open the calculator (basic, scientific and programming modes).";

    // Ctrl+A. Key constructor order is (consoleKey, shift, alt, control).
    // Was Ctrl+C, freed up so QuitCommand could bind it as an alternate to F10.
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.A, false, false, true);
}
