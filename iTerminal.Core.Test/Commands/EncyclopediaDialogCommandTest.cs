using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Encyclopedia;
using iTerminal.Core.Services.Encyclopedia;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Test.Services.Currency;
using iTerminal.Core.Test.Services.Encyclopedia;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class EncyclopediaDialogCommandTest
{
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
