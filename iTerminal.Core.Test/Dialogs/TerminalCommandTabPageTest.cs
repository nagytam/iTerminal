using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class TerminalCommandTabPageTest
{
    [TestMethod]
    public async Task SummarisesTheLastCommand()
    {
        var start = new DateTime(2026, 9, 28, 10, 0, 0);
        var page = new TerminalCommandTabPage(TerminalOutputTabPageTest.ModelWith(new TerminalCommand
        {
            Command = "git status",
            ExitCode = 1,
            WorkingDirectoryUrl = new Url(Path.GetTempPath()),
            StartTime = start,
            ExitTime = start.AddMilliseconds(250),
            Host = "localhost",
            Username = "me",
        }), TestTheme());

        await page.OnInitializeAsync();

        var lines = page.LinesForTest.ToList();
        CollectionAssert.Contains(lines, "Command: git status");
        CollectionAssert.Contains(lines, "Exit code: 1");
        CollectionAssert.Contains(lines, "Start time: 2026-09-28 10:00:00");
        CollectionAssert.Contains(lines, "Elapsed time: 250 [msec]");
        CollectionAssert.Contains(lines, "Host: localhost");
        CollectionAssert.Contains(lines, "Username: me");
    }

    [TestMethod]
    public async Task WithNoHistoryThePageSaysSo()
    {
        var page = new TerminalCommandTabPage(TerminalOutputTabPageTest.ModelWith(), TestTheme());

        await page.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { TerminalTextTabPage.NoCommandText }, page.LinesForTest.ToArray());
    }

    [TestMethod]
    public void NameAndHotKey()
    {
        var page = new TerminalCommandTabPage(TerminalOutputTabPageTest.ModelWith(), TestTheme());

        Assert.AreEqual("4-Command", page.Name);
        Assert.AreEqual(ConsoleKey.D4, page.HotConsoleKey);
    }
}
