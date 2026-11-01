using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iConsole.Desktops;
using iConsole.Screens;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class ScreenManagerDialogTest
{
    private static Screen NewScreen(string name) =>
        new(NullConsoleDriver.Instance, TestTheme()) { Name = name, ViewSize = new Size(120, 40) };

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListsTheDesktopsScreensAndMarksTheCurrentOne()
    {
        var main = NewScreen("Main");
        var other = NewScreen("Logs");
        var desktop = new Mock<IDesktop>();
        desktop.SetupGet(d => d.Screens).Returns([main, other]);
        main.Desktop = desktop.Object;

        var dialog = new ScreenManagerDialog(TestTheme());
        await main.ShowDialogAsync(dialog);

        CollectionAssert.AreEqual(new[] { " Main  (current)", " Logs" }, dialog.RowsForTest.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AScreenWithNoDesktopYetGivesAnEmptyListNotACrash()
    {
        // Desktop is only set once a screen joins one; the dialog used to dereference it regardless.
        var screen = NewScreen("Main");
        var dialog = new ScreenManagerDialog(TestTheme());

        await screen.ShowDialogAsync(dialog);

        Assert.AreEqual(0, dialog.RowsForTest.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OutsideAScreenTheListSaysItIsEmpty()
    {
        var dialog = new ScreenManagerDialog(TestTheme());
        await ShowAsync(dialog);

        Assert.AreEqual("Screen manager", dialog.Title);
        AssertScreenContains(Render(dialog), "No screens.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SaysTheListIsForLookingAt()
    {
        var dialog = new ScreenManagerDialog(TestTheme());
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "one at a time");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var dialog = new ScreenManagerDialog(TestTheme());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}
