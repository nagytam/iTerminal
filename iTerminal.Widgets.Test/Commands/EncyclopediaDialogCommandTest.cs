using iTerminal.Widgets.Commands;
using iTerminal.Core.Test.Helper;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Widgets.Dialogs.Encyclopedia;
using iTerminal.Widgets.Services.Encyclopedia;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Widgets.Test.Services.Currency;
using iTerminal.Widgets.Test.Services.Encyclopedia;
using Moq;

namespace iTerminal.Widgets.Test.Commands;

[TestClass]
public class EncyclopediaDialogCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ItIsBoundToCtrlShiftW()
    {
        var dialog = new EncyclopediaDialog(
            new EncyclopediaService(new EncyclopediaTest.FakeEncyclopedia()), new CurrencyTest.MemorySettings(), Mock.Of<IFileLauncher>(),
            Mock.Of<IBeepService>(), new DateTimeProviderService(), iTerminal.Core.Test.Helper.UiTestHelper.TestTheme());

        var command = new EncyclopediaDialogCommand(dialog);

        Assert.AreEqual("Encyclopedia", command.Title);
        Assert.AreEqual(new Key(ConsoleKey.W, true, false, true), command.Key);
    }
}
