using iConsole.Data;
using iTerminal.Core.Dialogs.UnitConverter;

namespace iTerminal.Core.Commands;

public class UnitConverterDialogCommand(UnitConverterDialog unitConverterDialog) : ShowDialogCommand<UnitConverterDialog>(unitConverterDialog)
{
    public override string Title => "Unit Converter";
    public override string Description => "Open the unit converter (length, mass, temperature and more).";

    // Alt+Shift+F5. Not Ctrl+E: that is the Edit menu's hot key, which a file panel must open.
    // Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F5, true, true, false);
}
