using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// The arithmetic behind the controls that had no tests: where a slider's thumb sits and what a click on the
/// track means, the step glyphs, which timeline events fit, how a breadcrumb folds, how a countdown reads and
/// how a rating clamps.
/// </summary>
[TestClass]
public class ControlPureLogicTest
{
    private static TestThemeService Theme() => new();

    [TestMethod]
    public void Slider_ThumbColumnSpansTheTrackEndToEnd()
    {
        Assert.AreEqual(0, Slider.ThumbColumn(0, 0, 100, 11));
        Assert.AreEqual(5, Slider.ThumbColumn(50, 0, 100, 11));
        Assert.AreEqual(10, Slider.ThumbColumn(100, 0, 100, 11));
        Assert.AreEqual(0, Slider.ThumbColumn(50, 0, 100, 1), "a one-cell track has nowhere to put the thumb");
        Assert.AreEqual(0, Slider.ThumbColumn(50, 10, 10, 11), "an empty range has one position");
    }

    [TestMethod]
    public void Slider_ClickingACellPicksTheValueItStandsFor()
    {
        Assert.AreEqual(0, Slider.ValueAt(0, 0, 100, 11));
        Assert.AreEqual(50, Slider.ValueAt(5, 0, 100, 11));
        Assert.AreEqual(100, Slider.ValueAt(10, 0, 100, 11));
        Assert.AreEqual(-20, Slider.ValueAt(0, -20, 20, 11), "the range may start below zero");
        Assert.AreEqual(-20, Slider.ValueAt(3, -20, 20, 1));
    }

    [TestMethod]
    public async Task Slider_StepsClampAtTheBoundsWithoutWrapping()
    {
        var slider = new Slider(Theme()) { Minimum = 0, Maximum = long.MaxValue, Step = 5 };
        slider.SetFocus();
        slider.Value = long.MaxValue;

        await slider.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));

        Assert.AreEqual(long.MaxValue, slider.Value, "a step past the largest value stays there, it does not wrap to the bottom");
    }

    [TestMethod]
    public void StepIndicator_RendersDoneCurrentAndUpcomingSteps()
    {
        Assert.AreEqual("●─◉─○", StepIndicator.Render(3, 1));
        Assert.AreEqual("●─●─●", StepIndicator.Render(3, 5), "past the last step every step is done");
        Assert.AreEqual("◉─○─○", StepIndicator.Render(3, 0));
        Assert.AreEqual(string.Empty, StepIndicator.Render(0, 0));
        Assert.AreEqual(5, StepIndicator.WidthFor(3));
        Assert.AreEqual(0, StepIndicator.WidthFor(0));
    }

    [TestMethod]
    public void StepIndicator_ClicksOnlyCountOnTheGlyphs()
    {
        var indicator = new StepIndicator(Theme()) { Count = 3 };

        Assert.AreEqual(0, indicator.StepAtColumn(0));
        Assert.AreEqual(-1, indicator.StepAtColumn(1), "a joint is not a step");
        Assert.AreEqual(2, indicator.StepAtColumn(4));
        Assert.AreEqual(-1, indicator.StepAtColumn(5), "past the last glyph");
        Assert.AreEqual(-1, indicator.StepAtColumn(-1));
    }

    [TestMethod]
    public void Timeline_ShowsTheNewestEventsThatFit()
    {
        Assert.AreEqual(0, Timeline.FirstVisibleEvent(0, 5));
        Assert.AreEqual(2, Timeline.FirstVisibleEvent(5, 5), "five rows hold three events");
        Assert.AreEqual(3, Timeline.FirstVisibleEvent(5, 4), "four rows hold two");
        Assert.AreEqual(0, Timeline.FirstVisibleEvent(2, 10), "everything fits");
    }

    [TestMethod]
    public void Breadcrumb_KeepsTheFirstAndLastSegmentsWhenTheTrailIsTooWide()
    {
        IReadOnlyList<string> trail = ["Home", "Projects", "iTerminal"];

        Assert.AreEqual(3, Breadcrumb.Layout(trail, 40).Count, "a trail that fits is shown whole");

        var folded = Breadcrumb.Layout(["a", "bb", "ccc", "dddd"], 12);
        CollectionAssert.AreEqual(new (int, string)[] { (0, "a"), (-1, "…"), (3, "dddd") }, folded.ToArray());

        var tight = Breadcrumb.Layout(trail, 15);
        CollectionAssert.AreEqual(new (int, string)[] { (-1, "…"), (2, "iTerminal") }, tight.ToArray());
    }

    [TestMethod]
    public void Countdown_FormatRoundsUpSoTheLastSecondStillShows()
    {
        Assert.AreEqual("01:30", Countdown.Format(TimeSpan.FromSeconds(90)));
        Assert.AreEqual("00:01", Countdown.Format(TimeSpan.FromSeconds(0.2)));
        Assert.AreEqual("00:00", Countdown.Format(TimeSpan.Zero));
        Assert.AreEqual("1:01:01", Countdown.Format(TimeSpan.FromSeconds(3661)));
    }

    [TestMethod]
    public void Countdown_ReportsFinishedOnceWhenTheTargetPasses()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var clock = new Mock<IDateTimeProviderService>();
        clock.SetupGet(c => c.Now).Returns(() => now);
        var countdown = new Countdown(Theme(), clock.Object);
        int finished = 0;
        countdown.Finished += (_, _) => finished++;

        countdown.Start(TimeSpan.FromSeconds(2));
        now = now.AddSeconds(3);
        countdown.OnIdle();
        countdown.OnIdle();

        Assert.IsTrue(countdown.IsFinished);
        Assert.AreEqual(1, finished);
    }

    [TestMethod]
    public void Rating_ShrinkingTheStarsClampsTheValue()
    {
        var rating = new Rating(Theme()) { Maximum = 5, Value = 5 };

        rating.Maximum = 3;

        Assert.AreEqual(3, rating.Value);
        Assert.AreEqual(3, rating.Maximum);
    }

    [TestMethod]
    public void SegmentedControl_AClickOnAVisibleOptionPicksIt()
    {
        var control = new SegmentedControl(Theme()) { Position = new Position(0, 0), ViewSize = new Size(14, 1) };
        control.Options = ["Day", "Week", "Month", "Year"];
        control.SelectedIndex = 3;

        var visible = control.VisibleSegments(14);

        Assert.IsTrue(visible.Any(s => s.Index == 3), "the chosen option is in the window");
        Assert.AreEqual(3, control.IndexAtColumn(visible.Single(s => s.Index == 3).Start + 1));
        Assert.AreEqual(-1, control.IndexAtColumn(7), "the gap between two options picks nothing");
    }
}
