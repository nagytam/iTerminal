using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Test.Controls;

[TestClass]
public class ScrollViewTest
{
    /// <summary>Row y reads (y*3+x) mod 10 at column x, so a shift in either direction changes what is shown.</summary>
    private sealed class Grid(IThemeService theme) : Control(theme)
    {
        public List<MouseEvent> Clicks { get; } = [];

        public int Draws { get; private set; }

        public override void Draw(IBuffer buffer)
        {
            Draws++;
            for (long y = 0; y < ViewSize.Height; y++)
            {
                string text = new([.. Enumerable.Range(0, (int)ViewSize.Width).Select(x => (char)('0' + ((y * 3) + x) % 10))]);
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + y), new Size(ViewSize.Width, 1),
                    new ConxelString(text, Color.White, Color.Black));
            }
        }

        public override Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
        {
            if (mouseEvent.LeftClicked && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                Clicks.Add(mouseEvent);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }

    private sealed class Field(IThemeService theme) : Control(theme)
    {
        public Field() : this(Theme())
        {
        }

        public override void Draw(IBuffer buffer)
        {
            buffer.Write(ZOrder, AbsolutePosition, new Size(ViewSize.Width, 1), new ConxelString("[field]", Color.White, Color.Black));
        }
    }

    private sealed class Form(IThemeService theme) : Control(theme)
    {
        public override void Draw(IBuffer buffer)
        {
            for (long y = 0; y < ViewSize.Height; y++)
            {
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + y), new Size(ViewSize.Width, 1),
                    new ConxelString(new string('.', (int)ViewSize.Width), Color.White, Color.Black));
            }

            base.Draw(buffer);
        }
    }

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static ScrollView Create(int width, int height, int contentWidth, int contentHeight, out Grid grid)
    {
        grid = new Grid(Theme()) { ViewSize = new Size(contentWidth, contentHeight) };
        return new ScrollView(Theme()) { ViewSize = new Size(width, height), Position = new Position(0, 0), Content = grid };
    }

    private static KeyEvent Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static string[] Draw(ScrollView view, int width, int height) => ScreenBuffer.Render(view, width, height).Lines.ToArray();

    [TestMethod]
    public void ContentThatFits_ShowsWithoutBars()
    {
        ScrollView view = Create(6, 3, 6, 3, out _);

        Assert.AreEqual("012345|345678|678901", string.Join("|", Draw(view, 6, 3)));
    }

    [TestMethod]
    public void TallContent_ClipsToTheViewAndDrawsAVerticalBar()
    {
        ScrollView view = Create(6, 3, 5, 12, out _);

        string[] lines = Draw(view, 6, 3);

        Assert.AreEqual("01234█", lines[0]);
        Assert.AreEqual("34567░", lines[1]);
        Assert.AreEqual("67890░", lines[2]);
    }

    [TestMethod]
    public void WideAndTallContent_DrawsBothBarsAndANotchInTheCorner()
    {
        ScrollView view = Create(6, 4, 12, 12, out _);

        string[] lines = Draw(view, 6, 4);

        Assert.AreEqual(5, view.Viewport.Width);
        Assert.AreEqual(3, view.Viewport.Height);
        Assert.AreEqual("01234█", lines[0]);
        Assert.AreEqual("██░░░", lines[3]);
    }

    [TestMethod]
    public void ScrollTo_ShowsTheShiftedPart_AndClampsToTheContent()
    {
        ScrollView view = Create(6, 3, 5, 12, out _);

        view.ScrollTo(0, 4);
        string[] lines = Draw(view, 6, 3);
        Assert.AreEqual("23456", lines[0][..5]);
        Assert.AreEqual(4, view.OffsetY);

        view.ScrollTo(99, 99);
        Assert.AreEqual(0, view.OffsetX);
        Assert.AreEqual(9, view.OffsetY);
        view.ScrollTo(-5, -5);
        Assert.AreEqual(0, view.OffsetY);
    }

    [TestMethod]
    public void ScrollBy_RaisesScrolled_OnlyWhenTheViewMoves()
    {
        ScrollView view = Create(6, 3, 5, 12, out _);
        int raised = 0;
        view.Scrolled += (_, _) => raised++;

        view.ScrollBy(0, 2);
        view.ScrollBy(0, -50);
        view.ScrollBy(0, -50);

        Assert.AreEqual(2, raised);
    }

    [TestMethod]
    public void NothingDrawnByTheContent_LeaksOutsideTheView()
    {
        ScrollView view = Create(6, 3, 30, 30, out _);
        ScreenBuffer screen = new(40, 40);
        view.Draw(screen);

        for (int y = 0; y < 40; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                if (x >= 6 || y >= 3)
                {
                    Assert.IsFalse(screen.IsWritten(x, y), $"({x},{y}) was written");
                }
            }
        }
    }

    [TestMethod]
    public async Task Keys_ScrollAViewWithStaticContent()
    {
        ScrollView view = Create(6, 4, 5, 20, out _);
        view.SetFocus();

        Assert.IsTrue(await view.HandleKeyEventAsync(Key(ConsoleKey.DownArrow)));
        Assert.AreEqual(1, view.OffsetY);
        Assert.IsTrue(await view.HandleKeyEventAsync(Key(ConsoleKey.PageDown)));
        Assert.AreEqual(4, view.OffsetY);
        Assert.IsTrue(await view.HandleKeyEventAsync(Key(ConsoleKey.End)));
        Assert.AreEqual(16, view.OffsetY);
        Assert.IsFalse(await view.HandleKeyEventAsync(Key(ConsoleKey.DownArrow)));
        Assert.IsTrue(await view.HandleKeyEventAsync(Key(ConsoleKey.Home)));
        Assert.AreEqual(0, view.OffsetY);
        Assert.IsFalse(await view.HandleKeyEventAsync(Key(ConsoleKey.UpArrow)));
        Assert.IsFalse(await view.HandleKeyEventAsync(Key(ConsoleKey.A)));
    }

    [TestMethod]
    public async Task Keys_ScrollSidewaysWhenTheContentIsWide()
    {
        ScrollView view = Create(6, 4, 20, 2, out _);
        view.SetFocus();

        Assert.IsTrue(await view.HandleKeyEventAsync(Key(ConsoleKey.RightArrow)));
        Assert.AreEqual(1, view.OffsetX);
        Assert.IsTrue(await view.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow)));
        Assert.AreEqual(0, view.OffsetX);
    }

    [TestMethod]
    public async Task UnfocusedView_IgnoresKeys()
    {
        ScrollView view = Create(6, 4, 5, 20, out _);

        Assert.IsFalse(await view.HandleKeyEventAsync(Key(ConsoleKey.DownArrow)));
        Assert.AreEqual(0, view.OffsetY);
    }

    [TestMethod]
    public async Task Wheel_ScrollsThreeRows()
    {
        ScrollView view = Create(6, 4, 5, 20, out _);

        Assert.IsTrue(await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 1), WheelDelta = -1 }));
        Assert.AreEqual(3, view.OffsetY);
        Assert.IsTrue(await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 1), WheelDelta = 1 }));
        Assert.AreEqual(0, view.OffsetY);
    }

    [TestMethod]
    public async Task Click_ReachesTheContentInItsOwnCoordinates()
    {
        ScrollView view = Create(6, 4, 5, 20, out Grid grid);
        view.Position = new Position(10, 5);
        view.ScrollTo(0, 7);

        bool handled = await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(12, 6), LeftClicked = true });

        Assert.IsTrue(handled);
        Assert.AreEqual(1, grid.Clicks.Count);
        Assert.AreEqual(12, grid.Clicks[0].Position.X);
        Assert.AreEqual(13, grid.Clicks[0].Position.Y);
    }

    [TestMethod]
    public async Task Click_OutsideTheView_IsNotHandled()
    {
        ScrollView view = Create(6, 4, 5, 20, out Grid grid);

        Assert.IsFalse(await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(20, 1), LeftClicked = true }));
        Assert.AreEqual(0, grid.Clicks.Count);
    }

    [TestMethod]
    public async Task Click_OnTheVerticalBar_JumpsAlongTheTrack()
    {
        ScrollView view = Create(6, 5, 5, 20, out Grid grid);
        Assert.AreEqual(0, grid.Clicks.Count);

        await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 4), LeftClicked = true });
        Assert.AreEqual(15, view.OffsetY);

        await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 0), IsDrag = true });
        Assert.AreEqual(0, view.OffsetY);
        Assert.AreEqual(0, grid.Clicks.Count);
    }

    [TestMethod]
    public void FocusMovingIntoTheContent_ScrollsItIntoView()
    {
        Form form = new(Theme()) { ViewSize = new Size(10, 30) };
        Field first = new() { Position = new Position(0, 0), ViewSize = new Size(8, 1), CanFocus = true };
        Field last = new() { Position = new Position(0, 25), ViewSize = new Size(8, 1), CanFocus = true };
        form.AddControl(first);
        form.AddControl(last);
        ScrollView view = new(Theme()) { ViewSize = new Size(12, 5), Position = new Position(0, 0), Content = form };
        ScreenBuffer screen = new(12, 5);
        view.Draw(screen);

        last.SetFocus();
        view.Draw(screen);

        Assert.AreEqual(21, view.OffsetY);
        Assert.IsFalse(view.CanFocus);
        Assert.AreEqual("[field]", screen.Line(4)[..7]);
    }

    [TestMethod]
    public void RepaintOnlyWhenTheContentChanged()
    {
        ScrollView view = Create(6, 3, 6, 3, out Grid grid);
        ScreenBuffer screen = new(6, 3);
        Assert.IsFalse(view.Render(screen));
        int draws = grid.Draws;

        Assert.IsTrue(view.Render(screen));
        Assert.AreEqual(draws, grid.Draws);

        grid.Refresh();
        Assert.IsFalse(view.Render(screen));
        Assert.AreEqual(draws + 1, grid.Draws);
    }

    [TestMethod]
    public void FitWidth_MakesTheContentAsWideAsTheView()
    {
        ScrollView view = Create(8, 3, 100, 10, out Grid grid);
        view.FitWidth = true;

        Draw(view, 8, 3);

        Assert.AreEqual(7, grid.ViewSize.Width);
        Assert.AreEqual(0, view.OffsetX);
    }

    [TestMethod]
    public void ContentSize_OverridesTheContentsOwn()
    {
        ScrollView view = Create(6, 3, 6, 3, out Grid grid);
        view.ContentSize = new Size(6, 9);

        Draw(view, 6, 3);

        Assert.AreEqual(9, grid.ViewSize.Height);
        Assert.AreEqual(9, view.ExtentSize.Height);
    }

    [TestMethod]
    public void HiddenBars_LeaveTheWholeViewForTheContent()
    {
        ScrollView view = Create(6, 3, 5, 12, out _);
        view.ShowScrollBars = false;

        string[] lines = Draw(view, 6, 3);

        Assert.AreEqual(6, view.Viewport.Width);
        Assert.AreEqual("01234", lines[0]);
    }

    [TestMethod]
    public void ReplacingTheContent_ResetsTheOffset()
    {
        ScrollView view = Create(6, 3, 5, 12, out _);
        view.ScrollTo(0, 5);

        view.Content = new Grid(Theme()) { ViewSize = new Size(5, 12) };

        Assert.AreEqual(0, view.OffsetY);
    }

    [TestMethod]
    public void NoContentAndEmptySizes_DrawNothingAndDoNotThrow()
    {
        ScrollView view = new(Theme()) { ViewSize = new Size(6, 3) };
        Draw(view, 6, 3);

        view.ViewSize = new Size(0, 0);
        view.Draw(new ScreenBuffer(6, 3));
        view.ScrollTo(5, 5);
        Assert.AreEqual(0, view.OffsetY);
    }

    [TestMethod]
    public void Thumb_IsProportionalAndStaysInsideTheTrack()
    {
        Assert.AreEqual((0L, 10L), ScrollView.Thumb(10, 5, 10, 0));
        Assert.AreEqual((0L, 5L), ScrollView.Thumb(10, 20, 10, 0));
        Assert.AreEqual((5L, 5L), ScrollView.Thumb(10, 20, 10, 10));
        Assert.AreEqual((0L, 1L), ScrollView.Thumb(10, 1000, 10, 0));
        Assert.AreEqual((0L, 0L), ScrollView.Thumb(0, 20, 10, 0));
    }

    [TestMethod]
    public void KeyHints_ListOnlyTheDirectionsThatScroll()
    {
        ScrollView tall = Create(6, 3, 5, 12, out _);
        ScrollView fits = Create(6, 3, 5, 3, out _);

        Assert.IsTrue(tall.GetKeyHints().Any(h => h.Key == "Up/Down"));
        Assert.IsFalse(tall.GetKeyHints().Any(h => h.Key == "Left/Right"));
        Assert.AreEqual(0, fits.GetKeyHints().Count());
    }
}
