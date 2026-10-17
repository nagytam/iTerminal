using iConsole.Data;
using iTerminal.Core.Dialogs.Stocks;

namespace iTerminal.Core.Commands;

public class StockBrowserDialogCommand(StockBrowserDialog stockBrowserDialog) : ShowDialogCommand<StockBrowserDialog>(stockBrowserDialog)
{
    public override string Title => "Stocks";
    public override string Description => "Open the stock browser: live prices for a watchlist of stocks, with stocks from any exchange to add and remove.";

    // Alt+F7. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F7, false, true, false);
}
