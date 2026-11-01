using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Diagnostics;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Markdown;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>Types and behaviours no test named before: the XY charts, the log view, border glyphs, the default models and the null loggers.</summary>
[TestClass]
public class ControlsReviewBatch35Test
{
    private readonly TestThemeService _theme = new();

    private static void DrawAt(Control control, int width, int height)
    {
        control.Position = new Position(0, 0);
        control.ViewSize = new Size(width, height);
        ScreenBuffer.Render(control, Math.Max(1, width), Math.Max(1, height));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void XyCharts_WithNoData_ShowTheEmptyText_AndAcceptANullOne()
    {
        foreach (XYChart chart in new XYChart[] { new ScatterChart(_theme), new Histogram(_theme), new CandlestickChart(_theme) })
        {
            chart.Position = new Position(0, 0);
            chart.ViewSize = new Size(20, 4);
            Assert.IsTrue(ScreenBuffer.Render(chart, 20, 4).Contains("No data"), chart.GetType().Name);

            chart.EmptyText = null!;
            DrawAt(chart, 20, 4);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void XyCharts_WithData_DrawAtEverySmallSize()
    {
        ScatterChart scatter = new(_theme) { Series = [new ScatterSeries("a", [(0, 0), (1, 5), (2, 3)])], ShowLegend = true };
        Histogram histogram = new(_theme) { Values = [1, 2, 2, 3, 3, 3, double.NaN] };
        CandlestickChart candles = new(_theme) { Candles = [new Candle("d1", 10, 12, 9, 11), new Candle("d2", 11, 13, 10, 10)] };

        foreach (XYChart chart in new XYChart[] { scatter, histogram, candles })
        {
            foreach ((int width, int height) in new[] { (0, 0), (1, 1), (2, 2), (5, 3), (40, 10) })
            {
                DrawAt(chart, width, height);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LogView_SplitsLines_TrimsToMaxLines_AndFiltersWithoutDeleting()
    {
        LogView log = new(_theme) { MaxLines = 3 };

        log.Append("one\ntwo\r\nthree\tfour", LogEntryLevel.Info);
        log.Append("warned", LogEntryLevel.Warning);

        Assert.AreEqual(3, log.Count);
        log.MinimumLevel = LogEntryLevel.Warning;
        Assert.AreEqual(1, log.VisibleEntries.Count);
        log.MinimumLevel = LogEntryLevel.Trace;
        log.FilterText = "WARN";
        Assert.AreEqual(1, log.VisibleEntries.Count);
        Assert.AreEqual(3, log.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LogView_NullTextAndNullFilter_AreAccepted_AndMaxLinesIsAtLeastOne()
    {
        LogView log = new(_theme);

        log.Append(null!);
        log.FilterText = null!;
        log.MaxLines = -5;

        Assert.AreEqual(1, log.MaxLines);
        DrawAt(log, 20, 3);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LogView_NotFollowing_CountsTheLinesThatArrive_AndFollowingClearsThem()
    {
        LogView log = new(_theme) { Follow = false };

        log.Append("a");
        log.Append("b");
        Assert.AreEqual(2, log.UnreadCount);

        log.Follow = true;
        Assert.AreEqual(0, log.UnreadCount);
        log.Clear();
        Assert.AreEqual(0, log.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderGlyphs_EveryStyleHasItsOwnSixGlyphs_AndNoneIsBlank()
    {
        HashSet<BorderGlyphs> seen = [];
        foreach (BorderStyle style in new[] { BorderStyle.Single, BorderStyle.Double, BorderStyle.Rounded, BorderStyle.Thick, BorderStyle.Dashed })
        {
            Assert.IsTrue(seen.Add(BorderGlyphs.For(style)), style.ToString());
        }

        BorderGlyphs none = BorderGlyphs.For(BorderStyle.None);
        Assert.AreEqual(new BorderGlyphs(' ', ' ', ' ', ' ', ' ', ' '), none);
        Assert.AreEqual(none, BorderGlyphs.For((BorderStyle)99));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LanguageInfo_DefaultsToEnglish()
    {
        LanguageInfo info = new();

        Assert.AreEqual("en", info.Code);
        Assert.AreEqual("English", info.NativeName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CalendarState_RaisesChangedOnlyWhenTheDateReallyChanges()
    {
        CalendarState state = new();
        int raised = 0;
        state.Changed += (_, _) => raised++;
        DateOnly other = state.SelectedDate.AddDays(3);

        state.Select(state.SelectedDate);
        state.Select(other);
        state.Select(other);
        state.Today = other;
        state.Today = other;

        Assert.AreEqual(2, raised);
        Assert.AreEqual(other, state.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkdownLine_KeepsItsParts()
    {
        MarkdownLine line = new([], MarkdownBlockKind.Paragraph, 2);

        Assert.AreEqual(2, line.Level);
        Assert.AreEqual(string.Empty, line.Language);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheNullLoggers_AreSingletons_AndSwallowEverything()
    {
        var commandLogger = NullCommandLogger.Instance;
        Assert.AreSame(commandLogger, NullCommandLogger.Instance);
        var screenLogger = NullScreenLogger.Instance;
        Assert.AreSame(screenLogger, NullScreenLogger.Instance);
        NullScreenLogger.Instance.Capture("label", new BufferLayer(0, new Size(1, 1), Color.White, Color.Black), null);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SystemStopwatchFactory_StartsARunningStopwatch()
    {
        IStopwatch stopwatch = SystemStopwatchFactory.Instance.StartNew();

        await Task.Delay(15);
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.ElapsedMilliseconds >= 5);
    }
}
