using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class RadioGroupTest
{
    private static RadioGroup<string> CreateControl(Mock<IThemeService>? themeService = null)
    {
        var control = new RadioGroup<string>((themeService ?? new Mock<IThemeService>()).Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3)
        };
        control.SetItems(["Small", "Medium", "Large"]);
        control.SetFocus();
        return control;
    }

    /// <summary>A key the focused control declines is offered to every control; an unfocused group must let it pass.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnfocusedGroupIgnoresTheArrows()
    {
        var control = new RadioGroup<string>(new Mock<IThemeService>().Object) { ViewSize = new Size(20, 3) };
        control.SetItems(["Small", "Medium", "Large"]);

        Assert.IsFalse(await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow)));
        Assert.AreEqual(0, control.SelectedIndex);
    }

    private static KeyEvent Key(ConsoleKey consoleKey) => new('\0', consoleKey, false, false, false);

    [Timeout(60_000)]
    [TestMethod]
    public void SetItemsSelectsFirstItem()
    {
        var control = CreateControl();
        Assert.AreEqual(0, control.SelectedIndex);
        Assert.AreEqual("Small", control.SelectedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ArrowKeysMoveSelectionExclusively()
    {
        var control = CreateControl();

        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(1, control.SelectedIndex);

        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(2, control.SelectedIndex);

        // Clamped at the last item.
        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(2, control.SelectedIndex);

        await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(1, control.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HomeAndEndJumpToBounds()
    {
        var control = CreateControl();

        await control.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(2, control.SelectedIndex);

        await control.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, control.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingOptionSelectsIt()
    {
        var control = CreateControl();

        // Vertically stacked: the third option sits on row y = 2.
        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 2), LeftClicked = true });

        Assert.AreEqual(2, control.SelectedIndex);
        Assert.AreEqual("Large", control.SelectedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SelectionChangedFiresOnChangeOnly()
    {
        var control = CreateControl();
        var raised = 0;
        control.SelectionChanged += (_, _) => raised++;

        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(1, raised);

        // Already at index 0 after Home; pressing Up stays clamped and must not refire.
        await control.HandleKeyEventAsync(Key(ConsoleKey.Home));
        raised = 0;
        await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(0, raised);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOnlyIgnoresInput()
    {
        var control = CreateControl();
        control.IsReadOnly = true;

        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 2), LeftClicked = true });

        Assert.AreEqual(0, control.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DrawRendersAllOptionsWithOneSelected()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 3));
        var control = new RadioGroup<string>(themeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3)
        };
        control.SetItems(["Small", "Medium", "Large"]);

        control.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("◉ Small"));   // first item selected by default
        Assert.IsTrue(content.Contains("○ Medium"));
        Assert.IsTrue(content.Contains("○ Large"));
    }
}
