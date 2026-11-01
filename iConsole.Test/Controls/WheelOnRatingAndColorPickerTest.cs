using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>The wheel over a rating changes the stars; over a colour picker it moves the cursor a row, as the arrows do.</summary>
[TestClass]
public class WheelOnRatingAndColorPickerTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Wheel(long x, long y, int delta) => new() { Position = new Position(x, y), WheelDelta = delta };

    [Timeout(60_000)]
    [TestMethod]
    public async Task Rating_WheelUpAddsAStar_DownTakesOneAway_AndItStopsAtTheEnds()
    {
        Rating rating = new(Theme()) { Position = new Position(0, 0), Maximum = 3 };

        Assert.IsTrue(await rating.HandleMouseEventAsync(Wheel(1, 0, 1)));
        Assert.AreEqual(1, rating.Value);
        await rating.HandleMouseEventAsync(Wheel(1, 0, 1));
        await rating.HandleMouseEventAsync(Wheel(1, 0, 1));
        await rating.HandleMouseEventAsync(Wheel(1, 0, 1));
        Assert.AreEqual(3, rating.Value);

        await rating.HandleMouseEventAsync(Wheel(1, 0, -1));
        Assert.AreEqual(2, rating.Value);
        for (int i = 0; i < 5; i++)
        {
            await rating.HandleMouseEventAsync(Wheel(1, 0, -1));
        }

        Assert.AreEqual(0, rating.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Rating_ReadOnlyOrDisabledOrElsewhere_IgnoresTheWheel()
    {
        Rating readOnly = new(Theme()) { Position = new Position(0, 0), Value = 2, IsReadOnly = true };
        Rating disabled = new(Theme()) { Position = new Position(0, 0), Value = 2, IsEnabled = false };
        Rating plain = new(Theme()) { Position = new Position(0, 0), Value = 2 };

        await readOnly.HandleMouseEventAsync(Wheel(1, 0, 1));
        await disabled.HandleMouseEventAsync(Wheel(1, 0, 1));
        Assert.IsFalse(await plain.HandleMouseEventAsync(Wheel(40, 5, 1)));

        Assert.AreEqual(2, readOnly.Value);
        Assert.AreEqual(2, disabled.Value);
        Assert.AreEqual(2, plain.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ColorPicker_TheWheelMovesTheCursorARow_WithoutChoosing()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        Color chosen = picker.SelectedColor;
        int start = picker.CursorIndex;

        Assert.IsTrue(await picker.HandleMouseEventAsync(Wheel(1, 0, -1)));
        Assert.AreEqual(start + ColorPicker.GridColumns, picker.CursorIndex);
        Assert.AreEqual(chosen, picker.SelectedColor, "choosing is a click, Enter or Space");

        await picker.HandleMouseEventAsync(Wheel(1, 0, 1));
        Assert.AreEqual(start, picker.CursorIndex);
        await picker.HandleMouseEventAsync(Wheel(1, 0, 1));
        Assert.IsTrue(picker.CursorIndex <= start, "it stops at the top row");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ColorPicker_ReadOnlyOrAwayFromIt_IgnoresTheWheel()
    {
        ColorPicker readOnly = new(Theme()) { Position = new Position(0, 0), IsReadOnly = true };
        ColorPicker plain = new(Theme()) { Position = new Position(0, 0) };
        int start = plain.CursorIndex;

        await readOnly.HandleMouseEventAsync(Wheel(1, 0, -1));
        Assert.IsFalse(await plain.HandleMouseEventAsync(Wheel(60, 30, -1)));

        Assert.AreEqual(start, readOnly.CursorIndex);
        Assert.AreEqual(start, plain.CursorIndex);
    }
}
