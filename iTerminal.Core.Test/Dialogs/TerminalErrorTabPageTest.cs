using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class TerminalErrorTabPageTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowsTheLastCommandsErrors()
    {
        var page = new TerminalErrorTabPage(TerminalOutputTabPageTest.ModelWith(
            new TerminalCommand { Output = "fine", Error = "file not found" }), TestTheme());

        await page.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { "file not found" }, page.LinesForTest.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASuccessfulCommandSaysThereWereNoErrors()
    {
        var page = new TerminalErrorTabPage(TerminalOutputTabPageTest.ModelWith(
            new TerminalCommand { Output = "fine", Error = "" }), TestTheme());

        await page.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { "(no errors)" }, page.LinesForTest.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NameAndHotKey()
    {
        var page = new TerminalErrorTabPage(TerminalOutputTabPageTest.ModelWith(), TestTheme());

        Assert.AreEqual("2-Error", page.Name);
        Assert.AreEqual(ConsoleKey.D2, page.HotConsoleKey);
    }
}
