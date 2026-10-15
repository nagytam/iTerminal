using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class IPhoneBrowserDialogCommand(IPhoneBrowserDialog iPhoneBrowserDialog)
    : ShowDialogCommand<IPhoneBrowserDialog>(iPhoneBrowserDialog)
{
    public override string Title => "iPhone Browser";
    public override string Description => "Browse a connected iPhone / iPad and mount it as a drive.";

    // Key ctor is (consoleKey, shift, alt, control) → Alt+F11. It used to be Alt+I, which never
    // arrived: the focused file panel claims every Alt+printable chord for type-to-search, so the
    // key opened a "Filter: i" instead. Ctrl+I is no better — terminals deliver it as Tab.
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F11, false, true, false);
}
