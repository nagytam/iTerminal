using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>Usability fixes from a review of the small controls: a remembered minus that outlived its entry, a rating that lost its width, and a section that detached its content behind the tree's back.</summary>
[TestClass]
public class ControlsReviewBatch20Test
{
    private readonly TestThemeService _theme = new();

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    [Timeout(60_000)]
    [TestMethod]
    public async Task NumericUpDown_ClickAfterAMinus_DropsTheRememberedSign()
    {
        NumericUpDown box = new(_theme) { Minimum = -100, Maximum = 100, Value = 0, Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        box.SetFocus();

        await box.HandleKeyEventAsync(new KeyEvent('-', ConsoleKey.OemMinus, false, false, false));
        await box.HandleMouseEventAsync(Click(15));
        await box.HandleKeyEventAsync(new KeyEvent('5', ConsoleKey.D5, false, false, false));

        Assert.AreEqual(5, box.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NumericUpDown_MinusThenDigit_StillGivesANegative()
    {
        NumericUpDown box = new(_theme) { Minimum = -100, Maximum = 100, Value = 0, Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        box.SetFocus();

        await box.HandleKeyEventAsync(new KeyEvent('-', ConsoleKey.OemMinus, false, false, false));
        await box.HandleKeyEventAsync(new KeyEvent('5', ConsoleKey.D5, false, false, false));

        Assert.AreEqual(-5, box.Value);
    }


    [Timeout(60_000)]
    [TestMethod]
    public void Expander_Collapsing_RemovesTheContentFromTheTree()
    {
        Expander expander = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        Label content = new("body", _theme);
        expander.Content = content;
        expander.IsExpanded = true;
        Assert.IsTrue(expander.Controls.Contains(content));

        expander.IsExpanded = false;

        Assert.IsFalse(expander.Controls.Contains(content));
        Assert.AreEqual(1, (int)expander.ViewSize.Height);
    }
}
