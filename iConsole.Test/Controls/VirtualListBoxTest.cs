using iConsole.Controls;
using iConsole.Data;
using iConsole.Model;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class VirtualListBoxTest
{
    private readonly TestThemeService _theme = new();

    /// <summary>A source of <c>count</c> rows that remembers what it was asked for.</summary>
    private sealed class CountingSource(int count) : IVirtualDataSource<string>
    {
        public int Count { get; set; } = count;
        public HashSet<int> Read { get; } = [];
        public List<(int Start, int Count)> Prefetched { get; } = [];
        public Func<int, bool> IsMissing { get; set; } = static _ => false;

        public event EventHandler? Changed;

        public bool TryGet(int index, out string? item)
        {
            Read.Add(index);
            if (index < 0 || index >= Count || IsMissing(index))
            {
                item = null;
                return false;
            }

            item = "row " + index;
            return true;
        }

        public void Prefetch(int start, int count) => Prefetched.Add((start, count));

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private VirtualListBox<string> Create(IVirtualDataSource<string> source, int width = 20, int height = 5)
    {
        VirtualListBox<string> list = new(source, _theme) { Position = new Position(0, 0), ViewSize = new Size(width, height) };
        list.SetFocus();
        return list;
    }

    private static KeyEvent Key(ConsoleKey key) => new(' ', key, false, false, false);

    [Timeout(60_000)]
    [TestMethod]
    public void DrawReadsOnlyTheRowsOnScreenEvenForTenMillionRows()
    {
        CountingSource source = new(10_000_000);
        VirtualListBox<string> list = Create(source);
        ScreenBuffer buffer = new(20, 5);

        list.Draw(buffer);

        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, source.Read.ToArray());
        Assert.AreEqual((0, 5), source.Prefetched.Last());
        Assert.IsTrue(buffer.Line(0).StartsWith("row 0", StringComparison.Ordinal));
        Assert.IsTrue(buffer.Line(4).StartsWith("row 4", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EndJumpsToTheLastRowOfTenMillionAndDrawsIt()
    {
        CountingSource source = new(10_000_000);
        VirtualListBox<string> list = Create(source);

        await list.HandleKeyEventAsync(Key(ConsoleKey.End));
        ScreenBuffer buffer = new(20, 5);
        source.Read.Clear();
        list.Draw(buffer);

        Assert.AreEqual(9_999_999, list.CursorIndex);
        Assert.AreEqual(9_999_995, list.TopIndex);
        Assert.IsTrue(buffer.Line(4).StartsWith("row 9999999", StringComparison.Ordinal));
        Assert.IsTrue(source.Read.All(index => index >= 9_999_995), "no row far from the screen was read");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ArrowsPagesAndHomeMoveTheCursorAndScroll()
    {
        VirtualListBox<string> list = Create(new CountingSource(100));

        await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(1, list.CursorIndex);
        Assert.AreEqual(0, list.TopIndex);

        await list.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(5, list.CursorIndex);
        Assert.AreEqual(1, list.TopIndex, "the cursor row is the last one on screen");

        await list.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(4, list.CursorIndex);

        await list.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, list.CursorIndex);
        Assert.AreEqual(0, list.TopIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheCursorStopsAtBothEnds()
    {
        VirtualListBox<string> list = Create(new CountingSource(3));

        await list.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(0, list.CursorIndex);

        await list.HandleKeyEventAsync(Key(ConsoleKey.End));
        await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(2, list.CursorIndex);
        Assert.AreEqual(0, list.TopIndex, "a list shorter than the screen does not scroll");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARowThatHasNotLoadedShowsThePlaceholder()
    {
        CountingSource source = new(100) { IsMissing = index => index == 2 };
        VirtualListBox<string> list = Create(source);
        ScreenBuffer buffer = new(20, 5);

        list.Draw(buffer);

        Assert.IsTrue(buffer.Line(1).StartsWith("row 1", StringComparison.Ordinal));
        Assert.IsTrue(buffer.Line(2).StartsWith("…", StringComparison.Ordinal));
        Assert.IsTrue(buffer.Line(3).StartsWith("row 3", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterRaisesItemActivatedWithTheCursorRow()
    {
        VirtualListBox<string> list = Create(new CountingSource(100));
        List<int> activated = [];
        list.ItemActivated += (_, index) => activated.Add(index);
        await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        await list.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { 2 }, activated);
        Assert.AreEqual("row 2", list.SelectedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickMovesTheCursorAndASecondClickActivates()
    {
        VirtualListBox<string> list = Create(new CountingSource(100));
        List<int> activated = [];
        list.ItemActivated += (_, index) => activated.Add(index);

        await list.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 2), LeftClicked = true });
        Assert.AreEqual(2, list.CursorIndex);
        Assert.AreEqual(0, activated.Count);

        await list.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 2), LeftClicked = true });
        CollectionAssert.AreEqual(new[] { 2 }, activated);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickBelowTheLastRowDoesNothing()
    {
        VirtualListBox<string> list = Create(new CountingSource(2));

        bool handled = await list.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 4), LeftClicked = true });

        Assert.IsFalse(handled);
        Assert.AreEqual(0, list.CursorIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheWheelMovesTheCursorByThreeRows()
    {
        VirtualListBox<string> list = Create(new CountingSource(100));

        await list.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 2), WheelDelta = -1 });
        Assert.AreEqual(3, list.CursorIndex);

        await list.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 2), WheelDelta = 1 });
        Assert.AreEqual(0, list.CursorIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheCursorIsBroughtBackWhenTheSourceShrinks()
    {
        CountingSource source = new(100);
        VirtualListBox<string> list = Create(source);
        list.MoveCursorTo(90);

        source.Count = 10;
        source.RaiseChanged();
        ScreenBuffer buffer = new(20, 5);
        list.Draw(buffer);

        Assert.AreEqual(9, list.CursorIndex);
        Assert.AreEqual(5, list.TopIndex);
        Assert.IsTrue(buffer.Line(4).StartsWith("row 9", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptySourceDrawsTheEmptyTextAndNothingElse()
    {
        VirtualListBox<string> list = Create(new CountingSource(0));
        list.EmptyText = "Nothing here";
        ScreenBuffer buffer = new(20, 5);

        list.Draw(buffer);

        Assert.IsTrue(buffer.Line(0).StartsWith("Nothing here", StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, buffer.Line(1).Trim());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RowsBelowTheLastAreRepaintedBlank()
    {
        CountingSource source = new(5);
        VirtualListBox<string> list = Create(source);
        ScreenBuffer buffer = new(20, 5);
        list.Draw(buffer);

        source.Count = 2;
        list.Draw(buffer);

        Assert.AreEqual(string.Empty, buffer.Line(3).Trim(), "a list that loses rows must not keep showing them");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnfocusedListTakesNoKeys()
    {
        CountingSource source = new(100);
        VirtualListBox<string> list = new(source, _theme) { Position = new Position(0, 0), ViewSize = new Size(20, 5) };

        bool handled = await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.IsFalse(handled);
        Assert.AreEqual(0, list.CursorIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACustomTextSelectorFormatsTheRows()
    {
        VirtualListBox<string> list = Create(new CountingSource(10));
        list.TextSelector = item => item.ToUpperInvariant();
        ScreenBuffer buffer = new(20, 5);

        list.Draw(buffer);
        await Task.CompletedTask;

        Assert.IsTrue(buffer.Line(0).StartsWith("ROW 0", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APagedSourceFillsInRowsAsTheyArrive()
    {
        // The page is held back until the first frame is drawn: a fetch that completes at once can beat that draw.
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PagedDataSource<string> source = new(
            async (start, count, _) =>
            {
                await gate.Task;
                return (IReadOnlyList<string>)[.. Enumerable.Range(start, count).Select(i => "item " + i)];
            },
            1_000_000, pageSize: 20, readAheadPages: 0);
        VirtualListBox<string> list = Create(source);
        ScreenBuffer buffer = new(20, 5);

        list.Draw(buffer);
        Assert.IsTrue(buffer.Line(0).StartsWith("…", StringComparison.Ordinal), "the first frame shows placeholders");
        gate.SetResult();

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!source.TryGet(0, out _) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        list.Draw(buffer);
        Assert.IsTrue(buffer.Line(0).StartsWith("item 0", StringComparison.Ordinal));
        source.Dispose();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollToMovesTheViewportAndLeavesTheCursorWhereItWas()
    {
        VirtualListBox<string> list = Create(new CountingSource(100));

        list.ScrollTo(50);
        ScreenBuffer buffer = new(20, 5);
        list.Draw(buffer);

        Assert.AreEqual(50, list.TopIndex);
        Assert.AreEqual(0, list.CursorIndex, "scrolling does not move the cursor");
        Assert.IsTrue(buffer.Line(0).StartsWith("row 50", StringComparison.Ordinal), "the viewport is not pulled back to the cursor");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollToIsClampedToTheLastFullPage()
    {
        VirtualListBox<string> list = Create(new CountingSource(100));

        list.ScrollTo(99);
        ScreenBuffer buffer = new(20, 5);
        list.Draw(buffer);

        Assert.AreEqual(95, list.TopIndex);
        Assert.IsTrue(buffer.Line(4).StartsWith("row 99", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MovingTheCursorBringsTheViewportBackToIt()
    {
        VirtualListBox<string> list = Create(new CountingSource(100));
        list.ScrollTo(50);

        await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.AreEqual(1, list.CursorIndex);
        Assert.AreEqual(1, list.TopIndex, "the viewport follows the cursor again: row 1 is now the first row on screen");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyListShowsTheEmptyTextAndNoRows()
    {
        VirtualListBox<string> list = Create(new CountingSource(0));
        list.EmptyText = "Nothing here";
        ScreenBuffer buffer = new(20, 5);

        list.Draw(buffer);

        Assert.IsTrue(buffer.Line(0).StartsWith("Nothing here", StringComparison.Ordinal));
        Assert.AreEqual(0, list.CursorIndex);
        Assert.IsNull(list.SelectedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyListTakesNoNavigationOrActivationKeys()
    {
        VirtualListBox<string> list = Create(new CountingSource(0));
        List<int> activated = [];
        list.ItemActivated += (_, index) => activated.Add(index);

        bool enter = await list.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        bool down = await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.IsFalse(enter);
        Assert.IsFalse(down);
        Assert.AreEqual(0, activated.Count);
    }
}
