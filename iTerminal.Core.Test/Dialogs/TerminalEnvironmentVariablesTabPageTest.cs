using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class TerminalEnvironmentVariablesTabPageTest
{
    [TestMethod]
    public async Task ShowsOneVariablePerLine()
    {
        var page = new TerminalEnvironmentVariablesTabPage(TerminalOutputTabPageTest.ModelWith(
            new TerminalCommand { EnvironmentVariables = "PATH=/bin\r\nHOME=/home/me" }), TestTheme());

        await page.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { "PATH=/bin", "HOME=/home/me" }, page.LinesForTest.ToArray());
    }

    [TestMethod]
    public async Task NoRecordedVariablesAreSaidRatherThanLeftBlank()
    {
        var page = new TerminalEnvironmentVariablesTabPage(TerminalOutputTabPageTest.ModelWith(new TerminalCommand()), TestTheme());

        await page.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { "(no environment variables were recorded)" }, page.LinesForTest.ToArray());
    }

    [TestMethod]
    public void NameAndHotKey()
    {
        var page = new TerminalEnvironmentVariablesTabPage(TerminalOutputTabPageTest.ModelWith(), TestTheme());

        Assert.AreEqual("3-Environment Variables", page.Name);
        Assert.AreEqual(ConsoleKey.D3, page.HotConsoleKey);
    }
}
