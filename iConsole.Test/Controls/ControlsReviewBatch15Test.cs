using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>Usability fixes from a review of the small controls: clicks past a grid, a highlight that hid on a narrow trail, disabled controls that kept their live colours, and values that broke the layout.</summary>
[TestClass]
public class ControlsReviewBatch15Test
{
    private readonly TestThemeService _theme = new();

    private ColorScheme Scheme => _theme.CurrentColorScheme;

    private sealed class FakeClock : IDateTimeProviderService
    {
        public DateTime Time { get; set; } = new(2026, 10, 18, 12, 0, 0);

        public DateTime Now => Time;

        public DateTime UtcNow => Time.ToUniversalTime();
    }

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    [TestMethod]
    public async Task ColorPicker_ClickRightOfTheGrid_DoesNotPickASwatchFromTheNextRow()
    {
        ColorPicker picker = new(_theme) { Position = new Position(0, 0) };
        picker.ViewSize = new Size(ColorPicker.GridColumns * ColorPicker.SwatchWidth + 6, picker.ViewSize.Height);
        Color before = picker.SelectedColor;

        await picker.HandleMouseEventAsync(Click(ColorPicker.GridColumns * ColorPicker.SwatchWidth + 1, 0));

        Assert.AreEqual(before, picker.SelectedColor);
    }

    [TestMethod]
    public async Task ColorPicker_ClickInsideTheGrid_StillPicks()
    {
        ColorPicker picker = new(_theme) { Position = new Position(0, 0) };

        await picker.HandleMouseEventAsync(Click(ColorPicker.SwatchWidth, 0));

        Assert.AreEqual(1, picker.CursorIndex);
    }

    [TestMethod]
    public void Breadcrumb_HighlightOnTheFirstSegment_StaysVisibleWhenNothingFits()
    {
        List<(int Index, string Text)> layout = Breadcrumb.Layout(["Home", "Projects", "iTerminal", "src"], 6, 0);

        Assert.AreEqual(0, layout[0].Index);
        Assert.AreEqual("Home", layout[0].Text);
    }

    [TestMethod]
    public void Breadcrumb_Disabled_DrawsInTheDisabledColour()
    {
        Breadcrumb breadcrumb = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(30, 1), IsEnabled = false };
        breadcrumb.Segments = ["Home", "Projects"];

        ScreenBuffer screen = ScreenBuffer.Render(breadcrumb, 30, 1);

        Assert.AreEqual(Scheme.DisabledForeground, screen.ForegroundAt(0, 0));
        Assert.AreEqual(Scheme.DisabledForeground, screen.ForegroundAt(8, 0));
    }

    [TestMethod]
    public void Pagination_Disabled_DrawsInTheDisabledColour()
    {
        Pagination pagination = new(_theme) { Position = new Position(0, 0), TotalItems = 100, IsEnabled = false };
        pagination.Page = 3;

        ScreenBuffer screen = ScreenBuffer.Render(pagination, 20, 1);

        Assert.AreEqual(Scheme.DisabledForeground, screen.ForegroundAt(0, 0));
        Assert.AreEqual(Scheme.DisabledForeground, screen.ForegroundAt(3, 0));
    }

    [TestMethod]
    public void Pagination_Enabled_KeepsTheNormalTextColour()
    {
        Pagination pagination = new(_theme) { Position = new Position(0, 0), TotalItems = 100 };

        ScreenBuffer screen = ScreenBuffer.Render(pagination, 20, 1);

        Assert.AreEqual(Scheme.Foreground, screen.ForegroundAt(3, 0));
    }

    [TestMethod]
    public void NumericUpDown_Disabled_WellIsDimmed()
    {
        NumericUpDown box = new(_theme) { Position = new Position(0, 0), Value = 5, IsEnabled = false };

        ScreenBuffer screen = ScreenBuffer.Render(box, 16, 1);

        Assert.AreEqual(Scheme.DisabledBackground, screen.BackgroundAt(1, 0));
        Assert.AreEqual(Scheme.DisabledForeground, screen.ForegroundAt(1, 0));
    }

    [TestMethod]
    public void NumericUpDown_Enabled_WellKeepsItsFieldColours()
    {
        NumericUpDown box = new(_theme) { Position = new Position(0, 0), Value = 5 };

        ScreenBuffer screen = ScreenBuffer.Render(box, 16, 1);

        Assert.AreEqual(Color.Yellow, screen.BackgroundAt(1, 0));
    }

    [TestMethod]
    public void CheckList_Disabled_CursorRowUsesTheDisabledColour()
    {
        CheckList list = new(_theme) { Position = new Position(0, 0), IsEnabled = false };
        list.SetItems(["one", "two"]);

        ScreenBuffer screen = ScreenBuffer.Render(list, 30, 5);

        Assert.AreEqual(Scheme.DisabledForeground, screen.ForegroundAt(5, 0));
    }

    [TestMethod]
    public void Meter_NaN_IsAnEmptyBarNotACrash()
    {
        Meter meter = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(10, 1), Value = 0.5 };
        meter.Value = double.NaN;

        ScreenBuffer screen = ScreenBuffer.Render(meter, 10, 1);

        Assert.AreEqual(0.0, meter.Value);
        Assert.AreEqual("░░░░░░░░░░", screen.Line(0));
    }

    [TestMethod]
    public void StepIndicator_Disabled_DrawsInTheDisabledColour()
    {
        StepIndicator steps = new(_theme) { Position = new Position(0, 0), Count = 3, Current = 1, IsEnabled = false };

        ScreenBuffer screen = ScreenBuffer.Render(steps, 5, 1);

        Assert.AreEqual(Scheme.DisabledForeground, screen.ForegroundAt(0, 0));
        Assert.AreEqual(Scheme.DisabledForeground, screen.ForegroundAt(2, 0));
    }

    [TestMethod]
    public void StepIndicator_Enabled_MarksTheCurrentStepInTheAccent()
    {
        StepIndicator steps = new(_theme) { Position = new Position(0, 0), Count = 3, Current = 1 };

        ScreenBuffer screen = ScreenBuffer.Render(steps, 5, 1);

        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(2, 0));
    }

    [TestMethod]
    public void Timeline_EventWithMissingText_DoesNotThrow()
    {
        Timeline timeline = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(30, 3) };
        timeline.SetEvents([new TimelineEvent(null!, null!), new TimelineEvent("10:05", "Tests passed")]);

        ScreenBuffer screen = ScreenBuffer.Render(timeline, 30, 3);

        Assert.IsTrue(screen.Contains("Tests passed"));
    }

    [TestMethod]
    public void Countdown_Disabled_DrawsInTheDisabledColour()
    {
        FakeClock clock = new();
        Countdown countdown = new(_theme, clock) { Position = new Position(0, 0), IsEnabled = false };
        countdown.Start(TimeSpan.FromMinutes(5));

        ScreenBuffer screen = ScreenBuffer.Render(countdown, 8, 1);

        Assert.AreEqual(Scheme.DisabledForeground, screen.ForegroundAt(0, 0));
    }
}
