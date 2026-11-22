using iTerminal.Widgets.Commands;
using iTerminal.Core.Test.Helper;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Widgets.Dialogs.CurrencyConverter;
using iTerminal.Widgets.Services.Currency;
using iTerminal.Widgets.Test.Services.Currency;
using Moq;

namespace iTerminal.Widgets.Test.Commands;

[TestClass]
public class CurrencyConverterDialogCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ItIsBoundToCtrlShiftF5()
    {
        var settings = new CurrencyTest.MemorySettings();
        var dialog = new CurrencyConverterDialog(
            new CurrencyService(new CurrencyTest.FakeProvider(), settings), settings, Mock.Of<IBeepService>(), new DateTimeProviderService(),
            iTerminal.Core.Test.Helper.UiTestHelper.TestTheme());

        var command = new CurrencyConverterDialogCommand(dialog);

        Assert.AreEqual("Currency Converter", command.Title);
        Assert.AreEqual(new Key(ConsoleKey.F5, true, false, true), command.Key);
    }
}
