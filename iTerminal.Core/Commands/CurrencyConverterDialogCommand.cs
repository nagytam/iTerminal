using iConsole.Data;
using iTerminal.Core.Dialogs.CurrencyConverter;

namespace iTerminal.Core.Commands;

public class CurrencyConverterDialogCommand(CurrencyConverterDialog currencyConverterDialog) : ShowDialogCommand<CurrencyConverterDialog>(currencyConverterDialog)
{
    public override string Title => "Currency Converter";
    public override string Description => "Open the currency converter: download the current exchange rates and convert between currencies.";

    // Ctrl+Shift+F5. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F5, true, false, true);
}
