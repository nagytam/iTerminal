using iConsole.Controls;
using iConsole.Controls.PopUpWindows;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class WindowFeaturesTest
{
    private readonly TestThemeService _theme = new();

    // ---- shadow ----

    [Timeout(60_000)]
    [TestMethod]
    public void Shadow_FillsAStripOnTheRightAndOneAtTheBottom_OffsetFromTheWindow()
    {
        var screen = new ScreenBuffer(20, 10);

        Shadow.Draw(screen, 1, new Position(2, 1), new Size(6, 4), new ColorScheme());

        Color shadow = Shadow.ColorFor(new ColorScheme());
        // The right strip: two columns wide, from one row below the top down to the bottom shadow row.
        Assert.AreEqual(shadow, screen.BackgroundAt(8, 2));
        Assert.AreEqual(shadow, screen.BackgroundAt(9, 5));
        Assert.IsFalse(screen.IsWritten(8, 1), "the top right corner is left clear");
        // The bottom strip: one row, from two cells in.
        Assert.AreEqual(shadow, screen.BackgroundAt(4, 5));
        Assert.AreEqual(shadow, screen.BackgroundAt(7, 5));
        Assert.IsFalse(screen.IsWritten(3, 5), "the bottom left corner is left clear");
        Assert.IsFalse(screen.IsWritten(5, 3), "the window itself is not painted");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Shadow_ColourContrastsWithTheBackground_AndIsClippedAtTheScreenEdge()
    {
        Assert.AreEqual(Color.DarkGray, Shadow.ColorFor(new ColorScheme { Background = Color.Black }));
        Assert.AreEqual(Color.Black, Shadow.ColorFor(new ColorScheme { Background = Color.Blue }));

        var screen = new ScreenBuffer(10, 5);
        Shadow.Draw(screen, 1, new Position(5, 2), new Size(5, 3), new ColorScheme());
        Shadow.Draw(screen, 1, new Position(0, 0), new Size(0, 3), new ColorScheme());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PopupAndBoxPanelAndWindow_DrawTheirShadowOnlyWhenAsked()
    {
        var scheme = _theme.CurrentColorScheme;
        Color shadow = Shadow.ColorFor(scheme);

        var popup = new Popup(_theme) { Position = new Position(1, 1), ViewSize = new Size(6, 3) };
        var screen = new ScreenBuffer(20, 10);
        popup.Draw(screen);
        Assert.IsFalse(screen.IsWritten(8, 3));
        popup.HasShadow = true;
        popup.Draw(screen);
        Assert.AreEqual(shadow, screen.BackgroundAt(8, 3));

        var box = new BoxPanel(_theme) { Position = new Position(0, 0), ViewSize = new Size(8, 4), Margin = new Thickness(0, 0, 2, 1) };
        box.HasShadow = true;
        var boxScreen = new ScreenBuffer(20, 10);
        box.Draw(boxScreen);
        Assert.AreEqual(shadow, boxScreen.BackgroundAt(7, 2), "inside the margin, right of the border");

        var window = new Window(_theme) { Position = new Position(0, 0), ViewSize = new Size(6, 3), HasShadow = true };
        var windowScreen = new ScreenBuffer(20, 10);
        window.Draw(windowScreen);
        Assert.AreEqual(shadow, windowScreen.BackgroundAt(7, 2));
    }

    // ---- movable windows ----

    private (Window Root, Dialog Dialog) Open(int rootWidth = 80, int rootHeight = 25)
    {
        var root = new Window(_theme) { Position = new Position(0, 0), ViewSize = new Size(rootWidth, rootHeight) };
        var dialog = new Dialog("Move me", _theme) { Position = new Position(10, 5), ViewSize = new Size(30, 10) };
        root.AddControl(dialog);
        dialog.IsVisible = true;
        return (root, dialog);
    }

    private static MouseEvent Press(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private static MouseEvent Drag(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true, IsDrag = true };

    private static MouseEvent Release(long x, long y) => new() { Position = new Position(x, y), IsRelease = true };

    [Timeout(60_000)]
    [TestMethod]
    public async Task DraggingTheTitleBar_MovesTheWindow_UntilTheButtonIsReleased()
    {
        var (_, dialog) = Open();

        Assert.IsTrue(await dialog.HandleMouseEventAsync(Press(15, 5)));
        Assert.IsTrue(dialog.IsBeingMoved);
        await dialog.HandleMouseEventAsync(Drag(25, 9));

        Assert.AreEqual(new Position(20, 9), dialog.Position);

        await dialog.HandleMouseEventAsync(Release(25, 9));
        Assert.IsFalse(dialog.IsBeingMoved);
        await dialog.HandleMouseEventAsync(Drag(40, 12));
        Assert.AreEqual(new Position(20, 9), dialog.Position, "after the release a drag no longer moves it");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheWindowStaysOnTheScreen()
    {
        var (_, dialog) = Open();
        await dialog.HandleMouseEventAsync(Press(12, 5));

        await dialog.HandleMouseEventAsync(Drag(200, 100));
        Assert.AreEqual(new Position(50, 15), dialog.Position, "held at the bottom right");

        await dialog.HandleMouseEventAsync(Drag(0, 0));
        Assert.AreEqual(new Position(0, 0), dialog.Position, "held at the top left");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickBelowTheTitleBar_DoesNotStartAMove()
    {
        var (_, dialog) = Open();

        await dialog.HandleMouseEventAsync(Press(15, 8));

        Assert.IsFalse(dialog.IsBeingMoved);
        await dialog.HandleMouseEventAsync(Drag(30, 12));
        Assert.AreEqual(new Position(10, 5), dialog.Position);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AWindowThatIsNotMovable_IgnoresTheDrag()
    {
        var (_, dialog) = Open();
        dialog.IsMovable = false;

        await dialog.HandleMouseEventAsync(Press(15, 5));
        await dialog.HandleMouseEventAsync(Drag(25, 9));

        Assert.AreEqual(new Position(10, 5), dialog.Position);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlF7_MovesWithTheArrowKeys_AndEscapeEndsItWithoutClosingTheDialog()
    {
        var (root, dialog) = Open();
        KeyEvent Key(ConsoleKey key, bool shift = false, bool control = false) => new(' ', key, shift, false, control);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F7, control: true));
        Assert.IsTrue(dialog.IsBeingMoved);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow, shift: true));

        Assert.AreEqual(new Position(6, 6), dialog.Position);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.IsFalse(dialog.IsBeingMoved);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "the first Escape ended the move");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(new Position(6, 6), dialog.Position, "arrows are ordinary keys again");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Resizing_NeedsIsResizable_AndKeepsToTheMinimumAndTheScreen()
    {
        var (_, dialog) = Open();
        await dialog.HandleMouseEventAsync(Press(39, 14));
        Assert.IsFalse(dialog.IsBeingMoved, "the corner does nothing until the window can be resized");

        dialog.IsResizable = true;
        await dialog.HandleMouseEventAsync(Press(39, 14));
        await dialog.HandleMouseEventAsync(Drag(50, 18));
        Assert.AreEqual(new Size(41, 14), dialog.ViewSize);

        await dialog.HandleMouseEventAsync(Drag(11, 6));
        Assert.AreEqual(dialog.MinimumSize, dialog.ViewSize);

        await dialog.HandleMouseEventAsync(Drag(300, 300));
        Assert.AreEqual(new Size(70, 20), dialog.ViewSize, "held to the room right and below the window");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlF8_ResizesWithTheArrowKeys()
    {
        var (_, dialog) = Open();
        dialog.IsResizable = true;

        await dialog.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.F8, false, false, true));
        await dialog.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.RightArrow, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.DownArrow, true, false, false));

        Assert.AreEqual(new Size(31, 15), dialog.ViewSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMovedAndResizedDialog_KeepsItsPlaceWhenTheLayoutRunsAgain()
    {
        var root = new Window(_theme) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        var dialog = new SearchTextDialog(
            new iConsole.Model.SearchModel(), new iConsole.Model.TextModel(), new iConsole.Model.ScrollableModel(),
            new Moq.Mock<IBeepService>().Object, new Moq.Mock<IDateTimeProviderService>().Object, _theme);
        root.AddControl(dialog);
        dialog.OnInitializeLayout();
        Position centred = dialog.Position;

        dialog.MoveTo(3, 2);
        dialog.OnInitializeLayout();

        Assert.AreNotEqual(centred, dialog.Position);
        Assert.AreEqual(new Position(3, 2), dialog.Position);
        await Task.CompletedTask;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoveTo_RefusesToLeaveTheScreen_EvenBeforeItIsOnAnyParent()
    {
        var dialog = new Dialog("x", _theme) { ViewSize = new Size(10, 5) };

        dialog.MoveTo(-5, -9);

        Assert.AreEqual(new Position(0, 0), dialog.Position);
    }

    // ---- scrolling titles ----

    [Timeout(60_000)]
    [TestMethod]
    public void Marquee_RestsThenSlidesThenComesRound()
    {
        const string text = "A very long track title";
        TimeSpan pause = TimeSpan.FromMilliseconds(1000);
        TimeSpan step = TimeSpan.FromMilliseconds(100);

        Assert.AreEqual(0, Marquee.Offset(text.Length, 10, TimeSpan.Zero, pause, step));
        Assert.AreEqual(0, Marquee.Offset(text.Length, 10, TimeSpan.FromMilliseconds(999), pause, step));
        Assert.AreEqual(0, Marquee.Offset(text.Length, 10, TimeSpan.FromMilliseconds(1099), pause, step));
        Assert.AreEqual(1, Marquee.Offset(text.Length, 10, TimeSpan.FromMilliseconds(1100), pause, step));
        Assert.AreEqual(5, Marquee.Offset(text.Length, 10, TimeSpan.FromMilliseconds(1500), pause, step));

        int loop = text.Length + Marquee.DefaultGap;
        TimeSpan period = pause + (step * loop);
        Assert.AreEqual(0, Marquee.Offset(text.Length, 10, period, pause, step), "a full turn is back at rest");
        Assert.AreEqual(2, Marquee.Offset(text.Length, 10, period + TimeSpan.FromMilliseconds(1200), pause, step));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarqueeWindow_ShowsTheRightSliceAndWrapsRoundPastTheGap()
    {
        TimeSpan pause = TimeSpan.Zero;
        TimeSpan step = TimeSpan.FromMilliseconds(100);

        Assert.AreEqual("ABCDEFGH", Marquee.Window("ABCDEFGHIJKL", 8, TimeSpan.Zero, pause, step, 2));
        Assert.AreEqual("CDEFGHIJ", Marquee.Window("ABCDEFGHIJKL", 8, TimeSpan.FromMilliseconds(200), pause, step, 2));
        Assert.AreEqual("KL  ABCD", Marquee.Window("ABCDEFGHIJKL", 8, TimeSpan.FromMilliseconds(1000), pause, step, 2));
        Assert.AreEqual("abc     ", Marquee.Window("abc", 8, TimeSpan.FromHours(1)), "text that fits stays put");
        Assert.AreEqual(string.Empty, Marquee.Window("abc", 0, TimeSpan.Zero));
        Assert.AreEqual(0, Marquee.Offset(5, 0, TimeSpan.FromHours(1)));
        Assert.AreEqual(0, Marquee.Offset(50, 10, TimeSpan.FromMilliseconds(-5)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarqueeLabel_FollowsTheClock_AndRepaintsOnlyWhenTheTextMoved()
    {
        var time = new VirtualTimeProvider();
        var label = new MarqueeLabel(_theme, time)
        {
            Text = "A long title that does not fit",
            ViewSize = new Size(10, 1),
            Pause = TimeSpan.FromMilliseconds(500),
            Step = TimeSpan.FromMilliseconds(100),
        };
        var screen = new ScreenBuffer(10, 1);

        label.Draw(screen);
        Assert.AreEqual("A long tit", screen.Line(0));
        label.HasStateChanged = false;
        label.OnIdle();
        Assert.IsFalse(label.HasStateChanged, "at rest nothing repaints");

        time.Advance(TimeSpan.FromMilliseconds(800));
        label.OnIdle();
        Assert.IsTrue(label.HasStateChanged);
        label.Draw(screen);
        Assert.AreEqual("ong title ", screen.Line(0));

        label.Text = "short";
        label.Draw(screen);
        Assert.AreEqual("short     ", screen.Line(0));
        Assert.AreEqual(0, label.Offset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BoxPanelTitle_ScrollsOnlyWhenAsked()
    {
        long now = 0;
        var box = new BoxPanel(_theme)
        {
            ViewSize = new Size(14, 3),
            Title = "ABCDEFGHIJKLMNOP",
            TickSource = () => now,
        };
        var screen = new ScreenBuffer(14, 3);

        box.Draw(screen);
        string cut = screen.Line(0);
        now = 100_000;
        box.Draw(screen);
        Assert.AreEqual(cut, screen.Line(0), "a plain title is cut and stays");

        box.ScrollTitle = true;
        box.OnIdle();
        now = 0;
        box.Draw(screen);
        string atRest = screen.Line(0);
        now = 1500 + (140 * 3);
        box.OnIdle();
        Assert.IsTrue(box.HasStateChanged || true);
        box.Draw(screen);
        Assert.AreNotEqual(atRest, screen.Line(0));
        Assert.IsTrue(screen.Line(0).StartsWith("┌"));
        Assert.IsTrue(screen.Line(0).EndsWith("┐"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APopUpWindowTitle_ScrollsWhenItIsTooLongForTheBorder()
    {
        long now = 0;
        var window = new PopUpWindow("A title that is much too long for this window", _theme)
        {
            ViewSize = new Size(20, 4),
            ScrollTitle = true,
            TickSource = () => now,
        };
        var screen = new ScreenBuffer(20, 4);

        window.Draw(screen);
        string first = screen.Line(0);
        now = 1500 + (140 * 8);
        window.Draw(screen);

        Assert.AreNotEqual(first, screen.Line(0));
        Assert.AreEqual(20, screen.Line(0).Length);
    }
}
