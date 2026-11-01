using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>F2: the last terminal command's output, errors, environment and summary, one tab each.</summary>
[TestClass]
public class TerminalDialogTest
{
    private static TerminalModel ModelWith(params TerminalCommand[] history)
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
    public async Task TheTitleNamesTheLastCommand()
    {
        var dialog = new TerminalDialog(ModelWith(new TerminalCommand { Command = "dir /s", Output = "x" }), TestTheme());

        await ShowAsync(dialog);

        Assert.AreEqual("Terminal - dir /s", dialog.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoHistoryTheTitleIsPlain()
    {
        var dialog = new TerminalDialog(ModelWith(), TestTheme());

        await ShowAsync(dialog);

        Assert.AreEqual("Terminal", dialog.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HasTheFourPagesInOrder()
    {
        var dialog = new TerminalDialog(ModelWith(), TestTheme());
        await ShowAsync(dialog);

        CollectionAssert.AreEqual(
            new[] { "1-Output", "2-Error", "3-Environment Variables", "4-Command" },
            dialog.TabForTest.TabPages.Select(p => p.Name).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ThePageKeysAreShown()
    {
        var dialog = new TerminalDialog(ModelWith(), TestTheme());
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "Ctrl+1..4");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheOutputPageShowsTheOutput()
    {
        var dialog = new TerminalDialog(ModelWith(new TerminalCommand { Command = "echo", Output = "hello world" }), TestTheme());
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "hello world");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BeforeAnyCommandThePageSaysSoRatherThanStayingBlank()
    {
        var dialog = new TerminalDialog(ModelWith(), TestTheme());
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "No terminal command has been run yet.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var dialog = new TerminalDialog(ModelWith(), TestTheme());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}
