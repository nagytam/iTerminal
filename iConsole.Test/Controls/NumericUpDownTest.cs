using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class NumericUpDownTest
{
    private static NumericUpDown CreateControl(Mock<IThemeService>? themeService = null)
    {
        themeService ??= new Mock<IThemeService>();
        var control = new NumericUpDown(themeService.Object)
        {
            Minimum = 0,
            Maximum = 100,
            Value = 5,
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };
        control.SetFocus();
        return control;
    }

    /// <summary>A key the focused control declines is offered to every control; an unfocused spinner must let it pass.</summary>
    [TestMethod]
    public async Task AnUnfocusedSpinnerIgnoresTheArrows()
    {
        var control = new NumericUpDown(new Mock<IThemeService>().Object) { Maximum = 100, Value = 5 };

        Assert.IsFalse(await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow)));
        Assert.AreEqual(5, control.Value);
    }

    private static KeyEvent Key(ConsoleKey consoleKey, char keyChar = '\0') =>
        new(keyChar, consoleKey, false, false, false);

    [TestMethod]
    public async Task ArrowKeysStepTheValue()
    {
        var control = CreateControl();
        control.Step = 1;

        await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(6, control.Value);

        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(5, control.Value);
    }

    [TestMethod]
    public async Task PageKeysUseLargeStepAndClampToBounds()
    {
        var control = CreateControl();
        control.LargeStep = 10;

        await control.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(15, control.Value);

        await control.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, control.Value);

        await control.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(100, control.Value);

        // Already at Maximum: another step stays clamped.
        await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(100, control.Value);
    }

    [TestMethod]
    public async Task TypingDigitsBuildsTheValue()
    {
        var control = CreateControl();
        control.Value = 0;

        await control.HandleKeyEventAsync(Key(ConsoleKey.D4, '4'));
        await control.HandleKeyEventAsync(Key(ConsoleKey.D2, '2'));
        Assert.AreEqual(42, control.Value);

        await control.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        Assert.AreEqual(4, control.Value);
    }

    [TestMethod]
    public void ValueIsClampedToTheRange()
    {
        var control = CreateControl();

        control.Value = 9999;
        Assert.AreEqual(100, control.Value);

        control.Value = -50;
        Assert.AreEqual(0, control.Value);
    }

    [TestMethod]
    public async Task WheelStepsTheValue()
    {
        var control = CreateControl();

        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = 1 });
        Assert.AreEqual(6, control.Value);

        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = -1 });
        Assert.AreEqual(5, control.Value);
    }

    [TestMethod]
    public async Task ReadOnlyIgnoresInput()
    {
        var control = CreateControl();
        control.IsReadOnly = true;

        await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = 1 });
        Assert.AreEqual(5, control.Value);
    }

    [TestMethod]
    public async Task ValueChangedFiresOnChangeOnly()
    {
        var control = CreateControl();
        var raised = 0;
        control.ValueChanged += (_, _) => raised++;

        await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(1, raised);

        // At Minimum already; stepping down stays clamped and should not raise.
        control.Value = 0;
        raised = 0;
        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(0, raised);
    }

    [TestMethod]
    public void DrawRendersLabelAndValue()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var control = new NumericUpDown(themeService.Object)
        {
            Label = "Count",
            Minimum = 0,
            Maximum = 100,
            Value = 42,
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        control.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Count:"));
        Assert.IsTrue(buffer.ContentAsString.Contains("42"));
    }
}
