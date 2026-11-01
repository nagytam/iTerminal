using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>A press on the track starts a drag: the thumb follows the pointer while the button is held, and a release ends it.</summary>
[TestClass]
public class SliderDragTest
{
    private static Slider NewSlider()
    {
        Mock<IThemeService> theme = new();
        theme.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return new Slider(theme.Object)
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Position = new Position(0, 0),
            ViewSize = new Size(21, 1),
        };
    }

    private static MouseEvent Press(long column) => new() { Position = new Position(column, 0), LeftClicked = true };

    private static MouseEvent Drag(long column) => new() { Position = new Position(column, 0), IsDrag = true };

    private static MouseEvent Release(long column) => new() { Position = new Position(column, 0), IsRelease = true };

    [Timeout(60_000)]
    [TestMethod]
    public async Task DraggingFollowsThePointerAcrossTheTrack()
    {
        Slider slider = NewSlider();

        await slider.HandleMouseEventAsync(Press(10));
        Assert.AreEqual(50, slider.Value);

        await slider.HandleMouseEventAsync(Drag(20));
        Assert.AreEqual(100, slider.Value);

        await slider.HandleMouseEventAsync(Drag(0));
        Assert.AreEqual(0, slider.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADragThatLeavesTheTrackStillMovesTheThumbToTheNearestEnd()
    {
        Slider slider = NewSlider();
        await slider.HandleMouseEventAsync(Press(10));

        await slider.HandleMouseEventAsync(Drag(500));
        Assert.AreEqual(100, slider.Value, "a pointer past the right edge stops at the maximum");

        await slider.HandleMouseEventAsync(Drag(-40));
        Assert.AreEqual(0, slider.Value, "a pointer past the left edge stops at the minimum");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AReleaseEndsTheDrag()
    {
        Slider slider = NewSlider();
        await slider.HandleMouseEventAsync(Press(10));
        await slider.HandleMouseEventAsync(Release(10));

        await slider.HandleMouseEventAsync(Drag(20));

        Assert.AreEqual(50, slider.Value, "after the release the pointer moves nothing");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APressOnTheValueReadoutDoesNotStartADrag()
    {
        Mock<IThemeService> theme = new();
        theme.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        var slider = new Slider(theme.Object)
        {
            Minimum = 0,
            Maximum = 100,
            Value = 30,
            ShowValue = true,
            Position = new Position(0, 0),
            ViewSize = new Size(25, 1),
        };

        // With the readout drawn, the track is the first 25 - 4 cells; a press in the readout must not jump or drag.
        await slider.HandleMouseEventAsync(Press(24));
        await slider.HandleMouseEventAsync(Drag(0));

        Assert.AreEqual(30, slider.Value);
    }
}
