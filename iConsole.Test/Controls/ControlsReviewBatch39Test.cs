using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>A layout worked out from a screen of a few cells comes to a negative size or position; nothing is negative, so it is empty.</summary>
[TestClass]
public class ControlsReviewBatch39Test
{
    private readonly TestThemeService _theme = new();

    [TestMethod]
    public void Size_NeverHoldsANegativeWidthOrHeight()
    {
        Size size = new(5, 5);

        size.Width = -3;
        size.Height = -1;

        Assert.AreEqual(0, size.Width);
        Assert.AreEqual(0, size.Height);
        Assert.IsTrue(size.IsEmpty);
        Assert.AreEqual(0, new Size(-4, -9).Width);
    }

    [TestMethod]
    public void Position_NeverHoldsANegativeColumnOrRow()
    {
        Position position = new(5, 5);

        position.X = -2;
        position.Y = -7;

        Assert.AreEqual(0, position.X);
        Assert.AreEqual(0, position.Y);
        Assert.AreEqual(0, new Position(-1, -1).Y);
    }

    [TestMethod]
    public void ABorderedControl_WithNoWidthOrHeight_DrawsNothing_AndDoesNotThrow()
    {
        foreach ((int width, int height) in new[] { (0, 0), (0, 3), (3, 0), (1, 1), (2, 1) })
        {
            Window window = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(width, height) };

            ScreenBuffer.Render(window, Math.Max(1, width), Math.Max(1, height));
        }
    }
}
