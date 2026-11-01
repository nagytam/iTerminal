using iConsole.Controls.Charts;
using iConsole.Controls.Progress;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class HeatmapProgressTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    [Timeout(60_000)]
    [TestMethod]
    public void Heatmap_Level_MapsTheRangeToFiveShades()
    {
        Assert.AreEqual(0, Heatmap.Level(0));
        Assert.AreEqual(0, Heatmap.Level(0.19));
        Assert.AreEqual(2, Heatmap.Level(0.5));
        Assert.AreEqual(4, Heatmap.Level(1));
        Assert.AreEqual(4, Heatmap.Level(7));
        Assert.AreEqual(0, Heatmap.Level(-3));
        Assert.AreEqual(0, Heatmap.Level(double.NaN));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Heatmap_Draw_ShowsLabelsAndShades()
    {
        MockBuffer buffer = new(new Size(20, 3));
        new Heatmap(Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3),
            RowLabels = ["Mon", "Tue"],
            ColumnLabels = ["A", "B", "C"],
            Values = [[0, 0.5, 1], [1, 1, 0]],
        }.Draw(buffer);
        string[] lines = Lines(buffer);
        Assert.AreEqual("    A B C", lines[0].TrimEnd());
        Assert.AreEqual("Mon ··▒▒██", lines[1].TrimEnd());
        Assert.AreEqual("Tue ████··", lines[2].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Heatmap_SmallerDataSet_LeavesNothingBehind()
    {
        MockBuffer buffer = new(new Size(20, 3));
        Heatmap map = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 3), Values = [[1, 1], [1, 1], [1, 1]] };
        map.Draw(buffer);
        map.Values = [[1]];
        map.Draw(buffer);
        string[] lines = Lines(buffer);
        Assert.AreEqual("██", lines[0].TrimEnd());
        Assert.AreEqual(string.Empty, lines[1].Trim());
        Assert.AreEqual(string.Empty, lines[2].Trim());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Heatmap_MoreColumnsThanFit_AreClipped()
    {
        MockBuffer buffer = new(new Size(6, 1));
        new Heatmap(Theme()) { Position = new Position(0, 0), ViewSize = new Size(6, 1), Values = [[1, 1, 1, 1, 1]] }.Draw(buffer);
        Assert.AreEqual("██████", Lines(buffer)[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBar_IndeterminateBar_BouncesBetweenTheEnds()
    {
        Assert.AreEqual("▬▬‧‧‧‧‧‧‧‧", ProgressBar.BuildIndeterminateBar(10, 0));
        Assert.AreEqual("‧‧‧‧‧‧‧‧▬▬", ProgressBar.BuildIndeterminateBar(10, 8));
        Assert.AreEqual("‧‧‧‧‧‧▬▬‧‧", ProgressBar.BuildIndeterminateBar(10, 10));
        Assert.AreEqual(10, ProgressBar.BuildIndeterminateBar(10, 123).Length);
        Assert.AreEqual(string.Empty, ProgressBar.BuildIndeterminateBar(0, 3));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBar_Indeterminate_AdvancesWithTheClockAndShowsNoPercent()
    {
        long now = 1000;
        ProgressBar bar = new(Theme())
        {
            Label = string.Empty,
            IsIndeterminate = true,
            TickSource = () => now,
            Position = new Position(0, 0),
            ViewSize = new Size(14, 1),
        };
        bar.OnIdle();
        Assert.AreEqual(1, bar.Phase);
        bar.OnIdle();
        Assert.AreEqual(1, bar.Phase);
        now += 100;
        bar.OnIdle();
        Assert.AreEqual(2, bar.Phase);

        MockBuffer buffer = new(new Size(14, 1));
        bar.Draw(buffer);
        string line = Lines(buffer)[0];
        Assert.IsFalse(line.Contains('%'));
        Assert.IsTrue(line.Contains('▬'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBar_Determinate_IsUnchangedAndDoesNotAnimate()
    {
        ProgressBar bar = new(Theme()) { Label = string.Empty, Max = 100, Current = 50, Position = new Position(0, 0), ViewSize = new Size(14, 1) };
        bar.OnIdle();
        Assert.AreEqual(0, bar.Phase);
        MockBuffer buffer = new(new Size(14, 1));
        bar.Draw(buffer);
        Assert.IsTrue(Lines(buffer)[0].Contains("50%"));
    }
}
