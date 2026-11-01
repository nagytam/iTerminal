using System.ComponentModel;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using Moq;

namespace iTerminal.Core.Test.Controls.Contents;

/// <summary>
/// Mouse opening and model swaps on the file panel's entry list. The keyboard paths are covered by
/// <see cref="ContentBrowserTest"/> and the integration tests.
/// </summary>
[TestClass]
public class ContentBrowserInteractionTest
{
    /// <summary>Stands in for the Enter binding (open folder / archive / run file) and counts its runs.</summary>
    private sealed class RecordingEnterCommand : Command
    {
        public int Runs { get; private set; }
        public int LastCursor { get; private set; } = -1;
        public Func<int>? CursorSource { get; init; }

        public override Key? Key { get; init; } = new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            Runs++;
            LastCursor = CursorSource?.Invoke() ?? -1;
            return Task.CompletedTask;
        }
    }

    private static (ContentBrowser Browser, Mock<IContentModel> Model, RecordingEnterCommand Enter, Func<long> Clock, Action<long> Advance) Create()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var taskManager = new Mock<TaskManager>(MockBehavior.Loose).Object;
        var terminalModel = new Mock<TerminalModel>(MockBehavior.Loose, taskManager, contentModelService).Object;
        var browser = new ContentBrowser("Panel", contentModelService, terminalModel, taskManager, new TestThemeService())
        {
            ViewSize = new Size(20, 12),
        };

        var entries = Enumerable.Range(0, 30).Select(_ => Mock.Of<IEntry>()).ToList();
        var model = new Mock<IContentModel>(MockBehavior.Loose);
        model.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        model.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        model.SetupProperty(m => m.ViewSize, new Size(18, 10));
        model.SetupProperty(m => m.ScrollableSize, new Size(18, entries.Count));
        model.SetupGet(m => m.FilteredEntries).Returns(entries);
        browser.ContentModel = model.Object;

        long now = 1_000;
        browser.TickSource = () => now;
        var enter = new RecordingEnterCommand { CursorSource = () => (int)model.Object.CursorPosition.Y };
        browser.AddCommand(enter);
        return (browser, model, enter, () => now, ms => now += ms);
    }

    /// <summary>A left click on list row <paramref name="row"/> (0-based, inside the border).</summary>
    private static MouseEvent ClickRow(int row) => new() { Position = new Position(3, row + 1), LeftClicked = true };

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASingleClickOnlyMovesTheCursor()
    {
        var (browser, model, enter, _, _) = Create();

        await browser.HandleMouseEventAsync(ClickRow(3));

        Assert.AreEqual(3, (int)model.Object.CursorPosition.Y);
        Assert.AreEqual(0, enter.Runs);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADoubleClickOpensTheRowLikeEnter()
    {
        // A click could only move the cursor; opening anything meant reaching for the keyboard.
        var (browser, _, enter, _, advance) = Create();

        await browser.HandleMouseEventAsync(ClickRow(4));
        advance(200);
        await browser.HandleMouseEventAsync(ClickRow(4));

        Assert.AreEqual(1, enter.Runs);
        Assert.AreEqual(4, enter.LastCursor, "the clicked row is the one opened");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TwoSlowClicksAreNotADoubleClick()
    {
        var (browser, _, enter, _, advance) = Create();

        await browser.HandleMouseEventAsync(ClickRow(4));
        advance((long)ContentBrowser.DoubleClickInterval.TotalMilliseconds + 1);
        await browser.HandleMouseEventAsync(ClickRow(4));

        Assert.AreEqual(0, enter.Runs);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClicksOnDifferentRowsAreNotADoubleClick()
    {
        var (browser, _, enter, _, advance) = Create();

        await browser.HandleMouseEventAsync(ClickRow(2));
        advance(100);
        await browser.HandleMouseEventAsync(ClickRow(5));

        Assert.AreEqual(0, enter.Runs);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AThirdQuickClickStartsANewPair()
    {
        var (browser, _, enter, _, advance) = Create();

        await browser.HandleMouseEventAsync(ClickRow(1));
        advance(100);
        await browser.HandleMouseEventAsync(ClickRow(1));
        advance(100);
        await browser.HandleMouseEventAsync(ClickRow(1));

        Assert.AreEqual(1, enter.Runs, "a triple click opens once, not twice");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RightClicksDoNotOpen()
    {
        var (browser, _, enter, _, advance) = Create();
        var rightClick = new MouseEvent { Position = new Position(3, 2), RightClicked = true };

        await browser.HandleMouseEventAsync(rightClick);
        advance(100);
        await browser.HandleMouseEventAsync(rightClick);

        Assert.AreEqual(0, enter.Runs);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InitializingTwiceSubscribesToTheModelOnce()
    {
        // OnInitializeAsync runs again on every model change and used to add a second handler, so
        // the panel repainted twice for every change of its model.
        var (browser, model, _, _, _) = Create();

        await browser.OnInitializeAsync();
        await browser.OnInitializeAsync();

        model.VerifyAdd(m => m.PropertyChanged += It.IsAny<PropertyChangedEventHandler>(), Times.Exactly(2));
        model.VerifyRemove(m => m.PropertyChanged -= It.IsAny<PropertyChangedEventHandler>(), Times.Exactly(2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangingModelUnsubscribesFromTheOldOne()
    {
        var old = new Mock<IContentModel>(MockBehavior.Loose);
        var replacement = new Mock<IContentModel>(MockBehavior.Loose);
        replacement.SetupGet(m => m.Name).Returns("Right");
        replacement.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        replacement.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        replacement.SetupProperty(m => m.ViewSize, new Size(18, 10));
        replacement.SetupProperty(m => m.ScrollableSize, new Size(18, 0));
        replacement.SetupGet(m => m.FilteredEntries).Returns([]);
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.SetupGet(s => s.ContentModels).Returns([replacement.Object]);
        var taskManager = new Mock<TaskManager>(MockBehavior.Loose).Object;
        var terminalModel = new Mock<TerminalModel>(MockBehavior.Loose, taskManager, contentModelService.Object).Object;
        var browser = new ContentBrowser("Panel", contentModelService.Object, terminalModel, taskManager, new TestThemeService())
        {
            ViewSize = new Size(20, 12),
            ContentModel = old.Object,
        };

        await browser.ChangeModelAsync("Right");

        old.VerifyRemove(m => m.PropertyChanged -= It.IsAny<PropertyChangedEventHandler>(), Times.AtLeastOnce);
        Assert.AreSame(replacement.Object, browser.ContentModel);
    }

    // ── The scroll bar, the wheel and the search filter ─────────────────────────────────────────

    /// <summary>A left click on the scroll bar (the right border column) at list row <paramref name="row"/>.</summary>
    private static MouseEvent ClickScrollBar(ContentBrowser browser, int row) =>
        new() { Position = new Position((int)browser.ViewSize.Width - 1, row + 1), LeftClicked = true };

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheScrollBarsBottomArrowMovesDownALine()
    {
        var (browser, model, _, _, _) = Create();

        await browser.HandleMouseEventAsync(ClickScrollBar(browser, 9));

        Assert.AreEqual(1, (int)model.Object.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheScrollBarsTopArrowMovesUpALine()
    {
        var (browser, model, _, _, _) = Create();
        model.Object.CursorPosition = new Position(0, 5);

        await browser.HandleMouseEventAsync(ClickScrollBar(browser, 0));

        Assert.AreEqual(4, (int)model.Object.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOnTheTrackJumpsInProportion()
    {
        var (browser, model, _, _, _) = Create();

        // Row 5 of a 10-row bar is 4 rows into its 8-row track (row 0 is the up arrow): half-way,
        // so half-way down rows 0..29.
        await browser.HandleMouseEventAsync(ClickScrollBar(browser, 5));

        Assert.AreEqual(14, (int)model.Object.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheWheelMovesTheCursorAndStopsAtTheEnds()
    {
        var (browser, model, _, _, _) = Create();

        await browser.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 3), WheelDelta = 1 });
        Assert.AreEqual(0, (int)model.Object.CursorPosition.Y, "already at the top");

        await browser.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 3), WheelDelta = -100 });
        Assert.AreEqual(29, (int)model.Object.CursorPosition.Y, "stops on the last row");
        Assert.AreEqual(20, (int)model.Object.ScrollPosition.Y, "with the last row at the bottom of the view");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheWheelOverAnEmptyFolderIsHarmless()
    {
        var (browser, model, _, _, _) = Create();
        model.SetupGet(m => m.FilteredEntries).Returns([]);

        var handled = await browser.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 3), WheelDelta = -1 });

        Assert.IsTrue(handled);
        Assert.AreEqual(0, (int)model.Object.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClicksOutsideThePanelAreLeftForOthers()
    {
        var (browser, _, _, _, _) = Create();

        var handled = await browser.HandleMouseEventAsync(new MouseEvent { Position = new Position(80, 30), LeftClicked = true });

        Assert.IsFalse(handled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AltAndALetterStartsASearchThatTypingExtendsAndEscapeEnds()
    {
        var (browser, model, _, _, _) = Create();
        model.SetupProperty(m => m.Filter, null);

        await browser.HandleKeyEventAsync(new KeyEvent('r', ConsoleKey.R, false, true, false));
        Assert.AreEqual("r", model.Object.Filter);

        await browser.HandleKeyEventAsync(new KeyEvent('e', ConsoleKey.E, false, false, false));
        Assert.AreEqual("re", model.Object.Filter);

        await browser.HandleKeyEventAsync(new KeyEvent('\b', ConsoleKey.Backspace, false, false, false));
        Assert.AreEqual("r", model.Object.Filter);

        await browser.HandleKeyEventAsync(new KeyEvent('\x1b', ConsoleKey.Escape, false, false, false));
        Assert.IsNull(model.Object.Filter);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FunctionKeysDoNotStartASearch()
    {
        var (browser, model, _, _, _) = Create();
        model.SetupProperty(m => m.Filter, null);

        await browser.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F5, false, true, false));

        Assert.IsNull(model.Object.Filter);
    }
}
