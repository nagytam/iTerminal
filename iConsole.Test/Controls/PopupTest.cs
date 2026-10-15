using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class PopupTest
{
    private static Mock<IThemeService> ThemeMock()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return themeService;
    }

    private sealed record Harness(Window Root, CheckBox Owner, Popup Popup);

    private static Harness CreateHarness()
    {
        var theme = ThemeMock().Object;
        var root = new Window(theme) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        var owner = new CheckBox(theme) { Position = new Position(5, 5), ViewSize = new Size(10, 1) };
        root.AddControl(owner);
        var popup = new Popup(theme) { ViewSize = new Size(20, 6) };
        return new Harness(root, owner, popup);
    }

    private static bool RootContains(Window root, Popup popup) =>
        root.Controls.AsSnapshot().Any(control => ReferenceEquals(control, popup));

    private static KeyEvent Key(ConsoleKey consoleKey) => new('\0', consoleKey, false, false, false);

    [TestMethod]
    public async Task ShowAttachesAndAnchorsBelowOwner()
    {
        var harness = CreateHarness();

        await harness.Popup.ShowAsync(harness.Owner);

        Assert.IsTrue(harness.Popup.IsOpen);
        Assert.IsTrue(RootContains(harness.Root, harness.Popup));
        // Owner is at (5,5) and one cell tall, so a Below popup opens at (5,6).
        Assert.AreEqual(5, (int)harness.Popup.Position.X);
        Assert.AreEqual(6, (int)harness.Popup.Position.Y);
    }

    [TestMethod]
    public async Task ShowElevatesAboveTheOwner()
    {
        var harness = CreateHarness();

        await harness.Popup.ShowAsync(harness.Owner);

        Assert.IsTrue(harness.Popup.ZOrder > harness.Owner.ZOrder);
    }

    [TestMethod]
    public async Task AbovePlacementSitsOnTopOfOwner()
    {
        var harness = CreateHarness();
        harness.Popup.Placement = PopupPlacement.Above;

        await harness.Popup.ShowAsync(harness.Owner);

        // Owner top is y=5; a 6-tall popup placed Above opens at y = 5 - 6, clamped to 0.
        Assert.AreEqual(0, (int)harness.Popup.Position.Y);
    }

    [TestMethod]
    public async Task EscapeClosesAndDetaches()
    {
        var harness = CreateHarness();
        await harness.Popup.ShowAsync(harness.Owner);

        await harness.Popup.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(harness.Popup.IsOpen);
        Assert.IsFalse(RootContains(harness.Root, harness.Popup));
    }

    [TestMethod]
    public async Task OutsideClickClosesButInsideClickDoesNot()
    {
        var harness = CreateHarness();
        await harness.Popup.ShowAsync(harness.Owner);

        // Popup occupies (5,6)..(25,12); a click inside must not dismiss it.
        await harness.Popup.HandleMouseEventAsync(new MouseEvent { Position = new Position(6, 7), LeftClicked = true });
        Assert.IsTrue(harness.Popup.IsOpen);

        await harness.Popup.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });
        Assert.IsFalse(harness.Popup.IsOpen);
    }

    [TestMethod]
    public async Task ClosingRestoresFocusToTheOwner()
    {
        var harness = CreateHarness();
        harness.Owner.SetFocus();
        Assert.IsTrue(harness.Owner.IsFocused);

        await harness.Popup.ShowAsync(harness.Owner);
        await harness.Popup.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(harness.Owner.IsFocused);
    }

    [TestMethod]
    public async Task ClosedEventFires()
    {
        var harness = CreateHarness();
        var raised = 0;
        harness.Popup.Closed += (_, _) => raised++;

        await harness.Popup.ShowAsync(harness.Owner);
        await harness.Popup.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public async Task NonModalShowLeavesFocusAndInputWithTheOwner()
    {
        var harness = CreateHarness();
        harness.Owner.SetFocus();
        harness.Popup.Modal = false;
        harness.Popup.CloseOnOutsideClick = false;

        await harness.Popup.ShowAsync(harness.Owner);

        Assert.IsTrue(harness.Popup.IsOpen);
        Assert.IsFalse(harness.Popup.IsModal);
        // Focus stays on the owner so it keeps receiving keystrokes.
        Assert.IsTrue(harness.Owner.IsFocused);

        // An outside click is ignored when click-away is disabled.
        await harness.Popup.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });
        Assert.IsTrue(harness.Popup.IsOpen);
    }

    [TestMethod]
    public void SetContentFillsTheInterior()
    {
        var popup = new Popup(ThemeMock().Object) { ViewSize = new Size(20, 6) };
        var content = new FormPanel(ThemeMock().Object);

        popup.SetContent(content);

        Assert.AreEqual(1, (int)content.Position.X);
        Assert.AreEqual(1, (int)content.Position.Y);
        Assert.AreEqual(18, (int)content.ViewSize.Width);
        Assert.AreEqual(4, (int)content.ViewSize.Height);
    }

    [TestMethod]
    public void DrawRendersTitleBorder()
    {
        var buffer = new MockBuffer(new Size(20, 6));
        var popup = new Popup(ThemeMock().Object)
        {
            Title = "Menu",
            Position = new Position(0, 0),
            ViewSize = new Size(20, 6)
        };

        popup.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Menu"));
    }
}
