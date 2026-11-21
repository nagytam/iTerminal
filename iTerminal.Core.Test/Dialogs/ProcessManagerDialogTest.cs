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
    private const int CpuColumn = 2;
    private const int MemoryColumn = 3;

    private static ProcessManagerDialog Create() =>
        new(Mock.Of<IBeepService>(), new ProcessService(), new DateTimeProviderService(), TestTheme(), new SystemTimeProvider());

    /// <summary>The dialog samples on a worker and applies the result from OnIdle, so the wait has to keep the idle pass running.</summary>
    private static Task WaitForDialogAsync(ProcessManagerDialog dialog, Func<bool> condition, string because) =>
        WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return condition();
        }, because);

    private static Task WaitForListAsync(ProcessManagerDialog dialog) =>
        WaitForDialogAsync(dialog, () => dialog.RowsForTest.Count > 2, "the process list to fill");

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListsTheRunningProcesses()
    {
        var dialog = Create();
        await ShowAsync(dialog);

        await WaitForListAsync(dialog);
        Assert.AreEqual("Process manager", dialog.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowsMemoryAndProcessorUseForEachProcess()
    {
        var dialog = Create();
        var root = await ShowAsync(dialog);

        // The processor share needs a second sample, so the first refresh after the list fills is the one that shows it.
        await WaitForDialogAsync(dialog, () => dialog.RowsForTest.Any(p => p.CpuPercent is not null), "a processor share to be measured");
        var screen = Render(dialog);
        AssertScreenContains(screen, "CPU");
        AssertScreenContains(screen, "Memory");
        // The list is sorted by processor use, so on a busy machine the test host is not on the first screen; check the rows.
        string own = Process.GetCurrentProcess().ProcessName;
        Assert.IsTrue(dialog.RowsForTest.Any(p => p.Name == own), $"expected the process list to contain '{own}'.");
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheBusiestProcessIsFirstByDefault()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitForDialogAsync(dialog, () => dialog.RowsForTest.Any(p => p.CpuPercent is not null), "a processor share to be measured");

        Assert.AreEqual(CpuColumn, dialog.SortColumnForTest);
        Assert.IsTrue(dialog.SortDescendingForTest);
        var cpu = dialog.RowsForTest.Select(p => p.CpuPercent ?? -1).ToList();
        CollectionAssert.AreEqual(cpu.OrderByDescending(v => v).ToList(), cpu);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFilterNarrowsTheList()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitForListAsync(dialog);
        var all = dialog.RowsForTest.Count;

        dialog.FilterBoxForTest.Text = Process.GetCurrentProcess().ProcessName;

        await WaitForDialogAsync(dialog, () => dialog.RowsForTest.Count > 0 && dialog.RowsForTest.Count < all, "the filter to apply");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFilterThatMatchesNothingSaysSo()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitForListAsync(dialog);

        dialog.FilterBoxForTest.Text = "no-process-is-called-this-" + Guid.NewGuid().ToString("N");

        await WaitForDialogAsync(dialog, () => dialog.RowsForTest.Count == 0, "the filter to empty the list");
        AssertScreenContains(Render(dialog), "No process matches the filter.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ArrowKeysMoveTheListWhileTheFilterHasFocus()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitForListAsync(dialog);
        Assert.IsTrue(dialog.FilterBoxForTest.IsFocused, "the filter has focus from the start");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.AreEqual(1, (int)dialog.TableForTest.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlRightAndCtrlDownChangeTheSortWhileTheTableHasFocus()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitForListAsync(dialog);
        dialog.TableForTest.SetFocus();

        // Right moves to Memory and keeps the order that suits it: the biggest first.
        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, true)));
        Assert.AreEqual(MemoryColumn, dialog.SortColumnForTest);
        Assert.IsTrue(dialog.SortDescendingForTest);

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, true)));
        Assert.IsFalse(dialog.SortDescendingForTest);

        // Right wraps around from the last column to the first.
        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, true)));
        Assert.AreEqual(0, dialog.SortColumnForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlLeftAndRightStayWithTheFilterWhileItHasFocus()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitForListAsync(dialog);
        int before = dialog.SortColumnForTest;

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, true));

        Assert.AreEqual(before, dialog.SortColumnForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheCursorStaysOnTheSameProcessWhenTheOrderChanges()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitForListAsync(dialog);
        dialog.TableForTest.SetFocus();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        int chosen = dialog.TableForTest.SelectedRow!.Id;

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, true));
        dialog.OnIdle();

        Assert.AreEqual(chosen, dialog.TableForTest.SelectedRow!.Id);
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
        dialog.TableForTest.ScrollableModel.PropertyChanged += (_, _) => changes++;

        dialog.FilterBoxForTest.Text = "x";
        dialog.OnIdle();

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
        await WaitForListAsync(dialog);
        int last = dialog.RowsForTest.Count - 1;

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, true)));
        Assert.AreEqual(last, (int)dialog.TableForTest.ScrollableModel.CursorPosition.Y);

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, true)));
        Assert.AreEqual(0, (int)dialog.TableForTest.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PlainEndStaysWithTheFilterBox()
    {
        var dialog = Create();
        await ShowAsync(dialog);
        await WaitForListAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false));

        Assert.AreEqual(0, (int)dialog.TableForTest.ScrollableModel.CursorPosition.Y);
    }
}
