using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class PopupListViewTest
{
    private static PopupListView CreateControl(Mock<IThemeService>? themeService = null)
    {
        var view = new PopupListView((themeService ?? new Mock<IThemeService>()).Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 4)
        };
        view.SetItems(["Red", "Green", "Blue"]);
        return view;
    }

    private static KeyEvent Key(ConsoleKey consoleKey) => new('\0', consoleKey, false, false, false);

    [TestMethod]
    public void SetItemsClampsTheHighlight()
    {
        var view = CreateControl();
        view.SetItems(["A", "B"], highlight: 9);
        Assert.AreEqual(1, view.Highlight);
    }

    [TestMethod]
    public async Task ArrowKeysMoveTheHighlight()
    {
        var view = CreateControl();

        await view.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(1, view.Highlight);

        await view.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(2, view.Highlight);

        await view.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, view.Highlight);
    }

    [TestMethod]
    public async Task EnterCommitsTheHighlightedRow()
    {
        var view = CreateControl();
        var committed = -1;
        view.Committed = index => committed = index;

        await view.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await view.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(1, committed);
    }

    [TestMethod]
    public async Task EscapeCancels()
    {
        var view = CreateControl();
        var cancelled = false;
        view.Cancelled = () => cancelled = true;

        await view.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(cancelled);
    }

    [TestMethod]
    public async Task ClickingARowCommitsIt()
    {
        var view = CreateControl();
        var committed = -1;
        view.Committed = index => committed = index;

        // Rows are at relative y = 0,1,2; click the third.
        await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 2), LeftClicked = true });

        Assert.AreEqual(2, committed);
    }

    [TestMethod]
    public async Task WheelMovesTheHighlight()
    {
        var view = CreateControl();

        await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), WheelDelta = -1 });
        Assert.AreEqual(1, view.Highlight);

        await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), WheelDelta = 1 });
        Assert.AreEqual(0, view.Highlight);
    }

    [TestMethod]
    public void DrawRendersRows()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 4));
        var view = new PopupListView(themeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 4)
        };
        view.SetItems(["Red", "Green", "Blue"]);

        view.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("Red"));
        Assert.IsTrue(content.Contains("Green"));
        Assert.IsTrue(content.Contains("Blue"));
    }
}
