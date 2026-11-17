using iConsole.Data;
using iTerminal.Core.Dialogs.Terminal;

namespace iTerminal.Core.Commands;

public class MarketTerminalDialogCommand(MarketTerminalDialog marketTerminalDialog) : ShowDialogCommand<MarketTerminalDialog>(marketTerminalDialog)
{
    public override string Title => "Market Terminal";
    public override string Description => "Open the market terminal: a command line to look up any stock, a ticker tape and the watchlist as a monitor.";

    // Alt+Shift+F7. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F7, true, true, false);
}
