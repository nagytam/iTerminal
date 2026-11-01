using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class ProcessManagerDialogTest
{
    private static ProcessManagerDialog Create() =>
        new(Mock.Of<IBeepService>(), new ProcessService(), new DateTimeProviderService(), TestTheme());

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListsTheRunningProcesses()
    {
        var dialog = Create();
        await ShowAsync(dialog);

        await WaitUntilAsync(() => dialog.ListBoxForTest.Count > 0, "the process list to fill");
        Assert.AreEqual("Process manager", dialog.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFilterNarrowsTheList()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitUntilAsync(() => dialog.ListBoxForTest.Count > 0, "the process list to fill");
        var all = dialog.ListBoxForTest.Count;

        dialog.FilterBoxForTest.Text = Process.GetCurrentProcess().ProcessName;

        await WaitUntilAsync(() => dialog.ListBoxForTest.Count > 0 && dialog.ListBoxForTest.Count < all, "the filter to apply");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFilterThatMatchesNothingSaysSo()
    {
        var dialog = Create();
        await ShowAsync(dialog);

        dialog.FilterBoxForTest.Text = "no-process-is-called-this-" + Guid.NewGuid().ToString("N");

        await WaitUntilAsync(() => dialog.ListBoxForTest.Count == 0, "the filter to empty the list");
        AssertScreenContains(Render(dialog), "No process matches the filter.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ArrowKeysMoveTheListWhileTheFilterHasFocus()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitUntilAsync(() => dialog.ListBoxForTest.Count > 2, "the process list to fill");
        Assert.IsTrue(dialog.FilterBoxForTest.IsFocused, "the filter has focus from the start");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.AreEqual(1, (int)dialog.ListBoxForTest.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReopeningDoesNotMultiplyTheFilterHandlers()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        dialog.OnInitializeLayout();
        dialog.OnInitializeLayout();
        var changes = 0;
        dialog.ListBoxForTest.ScrollableModel.PropertyChanged += (_, _) => changes++;

        dialog.FilterBoxForTest.Text = "x";

        // One rebuild per keystroke, however many times the dialog was laid out.
        Assert.IsTrue(changes <= 6, $"{changes} scroll-model changes for one keystroke");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var dialog = Create();
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlEndAndCtrlHomeJumpToTheLastAndFirstProcessWhileTheFilterHasFocus()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitUntilAsync(() => dialog.ListBoxForTest.Count > 2, "the process list to fill");
        int last = dialog.ListBoxForTest.Count - 1;

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, true)));
        Assert.AreEqual(last, (int)dialog.ListBoxForTest.ScrollableModel.CursorPosition.Y);

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, true)));
        Assert.AreEqual(0, (int)dialog.ListBoxForTest.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PlainEndStaysWithTheFilterBox()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitUntilAsync(() => dialog.ListBoxForTest.Count > 2, "the process list to fill");

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false));

        Assert.AreEqual(0, (int)dialog.ListBoxForTest.ScrollableModel.CursorPosition.Y);
    }
}
