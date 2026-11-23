using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Controls.Test;

[TestClass]
public class ControlTest
{
    private static Mock<IThemeService> ThemeMock()
    {
        var m = new Mock<IThemeService>();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_Default_ShouldCreateInstance()
    {
        var derived = new TestControl(ThemeMock().Object);

        Assert.IsNotNull(derived);
        Assert.IsInstanceOfType(derived, typeof(iConsole.Controls.Control));
        Assert.IsTrue(derived.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_WithChildLabel_ChildIsRendered()
    {
        var theme = ThemeMock();
        var buffer = new MockBuffer(new Size(20, 3));
        var parent = new TestControl(theme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3)
        };
        var child = new Label("Hello", theme.Object)
        {
            Position = new Position(0, 1),
            ViewSize = new Size(10, 1)
        };
        parent.AddControl(child);

        parent.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Hello"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ChildAtRow_RenderedAtCorrectPosition()
    {
        var theme = ThemeMock();
        var buffer = new MockBuffer(new Size(20, 3));
        var parent = new TestControl(theme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3)
        };
        var child = new Label("World", theme.Object)
        {
            Position = new Position(0, 2),
            ViewSize = new Size(10, 1)
        };
        parent.AddControl(child);

        parent.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[2].Contains("World"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_InvisibleChild_NotRendered()
    {
        var theme = ThemeMock();
        var buffer = new MockBuffer(new Size(20, 3));
        var parent = new TestControl(theme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3)
        };
        var child = new Label("Hidden", theme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1),
            IsVisible = false
        };
        parent.AddControl(child);

        parent.Draw(buffer);

        Assert.IsFalse(buffer.ContentAsString.Contains("Hidden"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetVisualState_Disabled_ReturnsDisabled()
    {
        var control = new TestControl(ThemeMock().Object) { IsEnabled = false };

        var state = control.GetVisualState();

        Assert.AreEqual(VisualState.Disabled, state);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetVisualState_Enabled_ReturnsNormal()
    {
        var control = new TestControl(ThemeMock().Object) { IsEnabled = true };

        var state = control.GetVisualState();

        Assert.AreEqual(VisualState.Normal, state);
    }

    /// <summary>
    /// A hidden control draws nothing, even when marked changed. A repaint from scratch marks the
    /// whole tree, and the status bar's hidden date pop-up then wrote its blank rectangle into a
    /// layer that an open menu shares — the panels vanished under every open menu.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void AHiddenControlIsNotDrawnEvenWhenMarkedChanged()
    {
        var theme = new TestThemeService();
        var root = new Control(theme) { ViewSize = new Size(10, 2) };
        var hidden = new Label("hidden", theme) { ViewSize = new Size(8, 1), IsVisible = false };
        root.AddControl(hidden);
        root.Refresh();
        hidden.HasStateChanged = true;

        var screen = new ScreenBuffer(10, 2);
        root.Render(screen);

        Assert.IsFalse(screen.Contains("hidden"));
        Assert.IsFalse(screen.IsWritten(0, 0), "not even blanks");

        hidden.IsVisible = true;
        root.Render(screen);
        Assert.IsTrue(screen.Contains("hidden"), "once shown it draws");
    }

    // A thread of its own: Task.Wait may run a queued task inline on the waiting thread, which would not be another thread.
    private static void OnAnotherThread(Action action)
    {
        var thread = new Thread(() => action());
        thread.Start();
        thread.Join();
    }

    /// <summary>A control that runs a callback inside Draw, to change its own state while it is being drawn.</summary>
    private sealed class DuringDrawControl(IThemeService themeService, Action<Control> duringDraw) : Control(themeService)
    {
        public override void Draw(IBuffer buffer) => duringDraw(this);
    }

    /// <summary>
    /// The renderer thread lays the controls out on a resize while a control renders. Render cleared the change flag after
    /// Draw, so that invalidation was lost and the new layout was never painted (QA-7).
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void AnInvalidationFromAnotherThreadDuringDraw_IsKeptForTheNextFrame()
    {
        var control = new DuringDrawControl(ThemeMock().Object, self => OnAnotherThread(() => self.HasStateChanged = true));
        control.HasStateChanged = true;

        control.Render(new ScreenBuffer(10, 2));

        Assert.IsTrue(control.HasStateChanged, "the change made while it drew still has to be painted");
    }

    /// <summary>A control that touches its own state in Draw is not repainted for ever: that stays swallowed, as it always was.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public void AnInvalidationFromTheDrawingThreadItself_IsStillSwallowed()
    {
        var control = new DuringDrawControl(ThemeMock().Object, self => self.HasStateChanged = true);
        control.HasStateChanged = true;

        control.Render(new ScreenBuffer(10, 2));

        Assert.IsFalse(control.HasStateChanged);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnInvalidationBetweenFrames_IsNotMistakenForOneDuringDraw()
    {
        var control = new DuringDrawControl(ThemeMock().Object, _ => { });
        control.HasStateChanged = true;
        control.Render(new ScreenBuffer(10, 2));
        Assert.IsFalse(control.HasStateChanged);

        OnAnotherThread(() => control.HasStateChanged = true);
        control.Render(new ScreenBuffer(10, 2));

        Assert.IsFalse(control.HasStateChanged, "a flag set before the draw is cleared by it");
    }
}

internal class TestControl : iConsole.Controls.Control
{
    public TestControl(IThemeService themeService) : base(themeService) { }
}
