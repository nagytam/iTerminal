using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Shared behaviour of the terminal dialog's text pages, driven through the output page.</summary>
[TestClass]
public class TerminalOutputTabPageTest
{
    internal static TerminalModel ModelWith(params TerminalCommand[] history)
    {
        var model = new TerminalModel(new TaskManager(), Mock.Of<IContentModelService>());
        foreach (var command in history)
        {
            model.History.Add(command);
        }

        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowsTheLastCommandsOutputLineByLine()
    {
        var page = new TerminalOutputTabPage(ModelWith(
            new TerminalCommand { Output = "old" },
            new TerminalCommand { Output = "one\r\ntwo\nthree\n" }), TestTheme());

        await page.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { "one", "two", "three" }, page.LinesForTest.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACommandWithNoOutputSaysSo()
    {
        var page = new TerminalOutputTabPage(ModelWith(new TerminalCommand { Output = null }), TestTheme());

        await page.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { "(the command wrote no output)" }, page.LinesForTest.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoHistoryThePageSaysSo()
    {
        var page = new TerminalOutputTabPage(ModelWith(), TestTheme());

        await page.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { TerminalTextTabPage.NoCommandText }, page.LinesForTest.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheViewerIsLaidOutEvenWhenThereIsNothingToShow()
    {
        // The viewer used to be shrunk to nothing, which is what left the page blank.
        var page = new TerminalOutputTabPage(ModelWith(), TestTheme()) { ViewSize = new Size(40, 10) };
        await page.OnInitializeAsync();

        page.OnInitializeLayout();

        var viewer = page.Controls.AsSnapshot().Single();
        Assert.AreEqual(new Size(38, 7), viewer.ViewSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NameAndHotKey()
    {
        var page = new TerminalOutputTabPage(ModelWith(), TestTheme());

        Assert.AreEqual("1-Output", page.Name);
        Assert.AreEqual(ConsoleKey.D1, page.HotConsoleKey);
    }
}
