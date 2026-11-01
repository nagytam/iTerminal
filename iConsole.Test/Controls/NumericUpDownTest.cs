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

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_ListDeleteKeysAndOmitSignWhenNoNegatives()
    {
        var keys = CreateControl().GetKeyHints().Select(h => h.Key).ToList();

        CollectionAssert.Contains(keys, "Backspace");
        CollectionAssert.Contains(keys, "Delete");
        CollectionAssert.DoesNotContain(keys, "-");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_ListSignFlipWhenRangeAllowsNegatives()
    {
        var control = CreateControl();
        control.Minimum = -10;

        var hints = control.GetKeyHints().ToList();

        Assert.AreEqual("Flip the sign", hints.Single(h => h.Key == "-").Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_EmptyWhenReadOnlyOrDisabled()
    {
        var readOnly = CreateControl();
        readOnly.IsReadOnly = true;
        var disabled = CreateControl();
        disabled.IsEnabled = false;

        Assert.AreEqual(0, readOnly.GetKeyHints().Count());
        Assert.AreEqual(0, disabled.GetKeyHints().Count());
    }

    /// <summary>A key the focused control declines is offered to every control; an unfocused spinner must let it pass.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnfocusedSpinnerIgnoresTheArrows()
    {
        var control = new NumericUpDown(new Mock<IThemeService>().Object) { Maximum = 100, Value = 5 };

        Assert.IsFalse(await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow)));
        Assert.AreEqual(5, control.Value);
    }

    private static KeyEvent Key(ConsoleKey consoleKey, char keyChar = '\0') =>
        new(keyChar, consoleKey, false, false, false);

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public void ValueIsClampedToTheRange()
    {
        var control = CreateControl();

        control.Value = 9999;
        Assert.AreEqual(100, control.Value);

        control.Value = -50;
        Assert.AreEqual(0, control.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WheelStepsTheValue()
    {
        var control = CreateControl();

        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = 1 });
        Assert.AreEqual(6, control.Value);

        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = -1 });
        Assert.AreEqual(5, control.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOnlyIgnoresInput()
    {
        var control = CreateControl();
        control.IsReadOnly = true;

        await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = 1 });
        Assert.AreEqual(5, control.Value);
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public async Task Delete_ResetsTheValueToZero()
    {
        var control = CreateControl();

        Assert.IsTrue(await control.HandleKeyEventAsync(Key(ConsoleKey.Delete)));

        Assert.AreEqual(0, control.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Delete_ClampsZeroToTheMinimum()
    {
        var control = CreateControl();
        control.Minimum = 3;
        control.Value = 50;

        await control.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        Assert.AreEqual(3, control.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Plus_MakesANegativeValuePositive_AndLeavesAPositiveOne()
    {
        var control = CreateControl();
        control.Minimum = -100;
        control.Value = -7;

        Assert.IsTrue(await control.HandleKeyEventAsync(Key(ConsoleKey.OemPlus, '+')));
        Assert.AreEqual(7, control.Value);

        Assert.IsFalse(await control.HandleKeyEventAsync(Key(ConsoleKey.OemPlus, '+')), "nothing to do on a positive value");
        Assert.AreEqual(7, control.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOnly_IgnoresDeleteAndPlus()
    {
        var control = CreateControl();
        control.Minimum = -100;
        control.Value = -7;
        control.IsReadOnly = true;

        await control.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        await control.HandleKeyEventAsync(Key(ConsoleKey.OemPlus, '+'));

        Assert.AreEqual(-7, control.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_DeleteSaysZeroWhenTheRangeHoldsZero()
    {
        var control = CreateControl();
        control.Minimum = -10;

        Assert.AreEqual("Set the value to 0", control.GetKeyHints().Single(h => h.Key == "Delete").Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task KeyHints_DeleteSaysNearestBoundWhenTheRangeLeavesZeroOut()
    {
        // Delete clamps 0 to the minimum here, so the hint must not promise a 0 the box can never show.
        var control = CreateControl();
        control.Minimum = 3;
        control.Value = 50;

        Assert.AreEqual("Set the value to the nearest bound", control.GetKeyHints().Single(h => h.Key == "Delete").Description);

        await control.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        Assert.AreEqual(3, control.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PendingMinusOnZeroShowsAsMinusZero()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var control = CreateControl(themeService);
        control.Minimum = -10;
        control.Value = 0;

        await control.HandleKeyEventAsync(Key(ConsoleKey.OemMinus, '-'));
        control.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("-0"), "the pending minus is visible before the digit");
        Assert.AreEqual(0, control.Value, "the sign is only taken with the next digit");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Maximum_WidenedWithTheValueUnchanged_RequestsARepaint()
    {
        // The well is padded to the widest bound, so a wider maximum must be drawn again even though the value stayed put.
        var control = CreateControl();
        control.HasStateChanged = false;

        control.Maximum = 100000;

        Assert.IsTrue(control.HasStateChanged);
        Assert.AreEqual(5, control.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Minimum_ChangedWithTheValueUnchanged_RequestsARepaint()
    {
        // A negative minimum adds the sign hint, which only a repaint shows.
        var control = CreateControl();
        control.HasStateChanged = false;

        control.Minimum = -5;

        Assert.IsTrue(control.HasStateChanged);
        Assert.IsTrue(control.GetKeyHints().Any(h => h.Key == "-"));
    }
}
