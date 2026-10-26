using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Test.Controls;

[TestClass]
public class SplitterTest
{
    private sealed class Fill(IThemeService theme, char c) : Control(theme)
    {
        public override void Draw(IBuffer buffer)
        {
            for (uint y = 0; y < ViewSize.Height; y++)
            {
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + y), new Size(ViewSize.Width, 1),
                    new ConxelString(new string(c, (int)ViewSize.Width), Color.White, Color.Black));
            }
        }
    }

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static Splitter Create(int width, int height, Orientation orientation = Orientation.Horizontal)
    {
        Splitter splitter = new(Theme()) { Orientation = orientation, ViewSize = new Size(width, height), Position = new Position(0, 0) };
        splitter.SetFirst(new Fill(Theme(), 'a'));
        splitter.SetSecond(new Fill(Theme(), 'b'));
        return splitter;
    }

    [TestMethod]
    public void ComputeFirstSize_SplitsWhatIsLeftAfterTheDivider()
    {
        Assert.AreEqual(5, Splitter.ComputeFirstSize(11, 0.5, 0, 0));
        Assert.AreEqual(0, Splitter.ComputeFirstSize(11, 0, 0, 0));
        Assert.AreEqual(10, Splitter.ComputeFirstSize(11, 1, 0, 0));
        Assert.AreEqual(0, Splitter.ComputeFirstSize(0, 0.5, 0, 0));
    }

    [TestMethod]
    public void ComputeFirstSize_RespectsMinimums_AndSharesWhenTheyCannotBothFit()
    {
        Assert.AreEqual(3, Splitter.ComputeFirstSize(21, 0.05, 3, 3));
        Assert.AreEqual(17, Splitter.ComputeFirstSize(21, 0.99, 3, 3));
        Assert.AreEqual(2, Splitter.ComputeFirstSize(5, 0.5, 4, 4));
        Assert.AreEqual(10, Splitter.ComputeFirstSize(21, double.NaN, 0, 0));
    }

    [TestMethod]
    public void Draw_SideBySide_PutsTheDividerBetweenThePanes()
    {
        ScreenBuffer screen = ScreenBuffer.Render(Create(9, 2), 9, 2);

        Assert.AreEqual("aaaa│bbbb", screen.Line(0));
        Assert.AreEqual("aaaa│bbbb", screen.Line(1));
    }

    [TestMethod]
    public void Draw_Stacked_PutsTheDividerBetweenThePanes()
    {
        ScreenBuffer screen = ScreenBuffer.Render(Create(4, 5, Orientation.Vertical), 4, 5);

        Assert.AreEqual("aaaa|aaaa|" + Border.HorizontalSingle.Repeat(4) + "|bbbb|bbbb", string.Join("|", screen.Lines));
    }

    [TestMethod]
    public void Draw_AfterMovingTheDivider_LeavesNoStaleCells()
    {
        Splitter splitter = Create(9, 1);
        ScreenBuffer screen = new(9, 1);
        splitter.Draw(screen);

        splitter.SetFirstSize(2);
        splitter.Draw(screen);

        Assert.AreEqual("aa│bbbbbb", screen.Line(0));
    }

    [TestMethod]
    public void Ratio_KeepsProportionWhenResized()
    {
        Splitter splitter = Create(21, 3);
        splitter.Ratio = 0.25;

        Assert.AreEqual(5, splitter.FirstSize);
        splitter.ViewSize = new Size(41, 3);
        Assert.AreEqual(10, splitter.FirstSize);
    }

    [TestMethod]
    public void Ratio_OutOfRangeAndNaN_AreClamped()
    {
        Splitter splitter = Create(21, 3);

        splitter.Ratio = 7;
        Assert.AreEqual(1, splitter.Ratio);
        splitter.Ratio = -1;
        Assert.AreEqual(0, splitter.Ratio);
        splitter.Ratio = double.NaN;
        Assert.AreEqual(0.5, splitter.Ratio);
    }

    [TestMethod]
    public async Task ArrowKeys_MoveTheDividerAlongTheAxis_AndRaiseSplitChanged()
    {
        Splitter splitter = Create(21, 3);
        splitter.SetFocus();
        int raised = 0;
        splitter.SplitChanged += (_, _) => raised++;

        Assert.IsTrue(await splitter.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false)));
        Assert.AreEqual(11, splitter.FirstSize);
        Assert.IsTrue(await splitter.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.LeftArrow, false, false, false)));
        Assert.IsTrue(await splitter.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.PageUp, false, false, false)));
        Assert.AreEqual(2, splitter.FirstSize);
        Assert.IsTrue(await splitter.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, false)));
        Assert.AreEqual(10, splitter.FirstSize);
        Assert.IsFalse(await splitter.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.UpArrow, false, false, false)), "wrong axis");
        Assert.AreEqual(4, raised);
    }

    [TestMethod]
    public async Task ArrowKeys_WithoutFocus_DoNothing()
    {
        Splitter splitter = Create(21, 3);

        Assert.IsFalse(await splitter.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false)));
        Assert.AreEqual(10, splitter.FirstSize);
    }

    [TestMethod]
    public async Task Drag_FromTheDivider_MovesItToThePointer_AndReleaseStops()
    {
        Splitter splitter = Create(21, 3);

        Assert.IsTrue(await splitter.HandleMouseEventAsync(new MouseEvent { Position = new Position(10, 1), LeftClicked = true }));
        Assert.IsTrue(await splitter.HandleMouseEventAsync(new MouseEvent { Position = new Position(4, 1), IsDrag = true, LeftClicked = true }));
        Assert.AreEqual(4, splitter.FirstSize);
        Assert.IsTrue(await splitter.HandleMouseEventAsync(new MouseEvent { Position = new Position(4, 1), IsRelease = true }));
        await splitter.HandleMouseEventAsync(new MouseEvent { Position = new Position(15, 1), IsDrag = true, LeftClicked = true });
        Assert.AreEqual(4, splitter.FirstSize, "a drag that did not start on the divider does nothing");
    }

    [TestMethod]
    public async Task Drag_PastTheEdges_StaysWithinTheMinimums()
    {
        Splitter splitter = Create(21, 3);
        splitter.MinFirstSize = 3;
        splitter.MinSecondSize = 3;

        await splitter.HandleMouseEventAsync(new MouseEvent { Position = new Position(10, 0), LeftClicked = true });
        await splitter.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), IsDrag = true });
        Assert.AreEqual(3, splitter.FirstSize);
        await splitter.HandleMouseEventAsync(new MouseEvent { Position = new Position(500, 0), IsDrag = true });
        Assert.AreEqual(17, splitter.FirstSize);
    }

    [TestMethod]
    public async Task Click_OnAPane_IsNotADrag()
    {
        Splitter splitter = Create(21, 3);

        await splitter.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 1), LeftClicked = true });
        await splitter.HandleMouseEventAsync(new MouseEvent { Position = new Position(6, 1), IsDrag = true, LeftClicked = true });

        Assert.AreEqual(10, splitter.FirstSize);
    }

    [TestMethod]
    public void SetFirst_Replacing_RemovesTheOldPane()
    {
        Splitter splitter = Create(9, 1);
        IControl old = splitter.First!;

        splitter.SetFirst(new Fill(Theme(), 'c'));

        Assert.IsFalse(splitter.Controls.AsSnapshot().Contains(old));
        Assert.AreEqual("cccc│bbbb", ScreenBuffer.Render(splitter, 9, 1).Line(0));
    }

    [TestMethod]
    public void Draw_EmptySize_DoesNotThrow()
    {
        Splitter splitter = new(Theme()) { ViewSize = new Size(0, 0) };

        splitter.Draw(new ScreenBuffer(5, 5));
    }

    [TestMethod]
    public void GetKeyHints_NamesTheKeysOfTheAxis()
    {
        CollectionAssert.Contains(Create(9, 3).GetKeyHints().Select(h => h.Key).ToArray(), "Left/Right");
        CollectionAssert.Contains(Create(9, 3, Orientation.Vertical).GetKeyHints().Select(h => h.Key).ToArray(), "Up/Down");
    }
}
