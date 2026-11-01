using iConsole.Controls;
using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>ComboBox keys and mouse, BreakdownChart and Gauge drawing, StatusDot sizing.</summary>
[TestClass]
public class ComboChartsAndIndicatorsInteractionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Key(ConsoleKey key, char c = '\0', bool alt = false) => new(c, key, false, alt, false);

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    // ---- ComboBox ----

    private static (Window Root, ComboBox<string> Combo) NewCombo()
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        ComboBox<string> combo = new(Theme()) { Position = new Position(2, 2), ViewSize = new Size(20, 1) };
        root.AddControl(combo);
        combo.SetItems(["Red", "Green", "Blue", "Grey"]);
        combo.SetFocus();
        return (root, combo);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComboBox_UpDownHomeEnd_ChangeTheSelectionWithoutOpening()
    {
        (_, ComboBox<string> combo) = NewCombo();
        await combo.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(0, combo.SelectedIndex);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(1, combo.SelectedIndex);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(3, combo.SelectedIndex);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(3, combo.SelectedIndex);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, combo.SelectedIndex);
        Assert.IsFalse(combo.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComboBox_AltDownF4AndSpace_OpenTheList()
    {
        foreach (Opener opener in new[] { new Opener(ConsoleKey.DownArrow, Alt: true), new Opener(ConsoleKey.F4), new Opener(ConsoleKey.Spacebar, ' ') })
        {
            (_, ComboBox<string> combo) = NewCombo();
            await combo.HandleKeyEventAsync(Key(opener.Console, opener.Char, opener.Alt));
            Assert.IsTrue(combo.IsOpen, opener.Console.ToString());
            combo.Close();
        }
    }

    private readonly record struct Opener(ConsoleKey Console, char Char = '\0', bool Alt = false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComboBox_TypeAhead_JumpsToTheNextMatchAndWraps()
    {
        (_, ComboBox<string> combo) = NewCombo();
        await combo.HandleKeyEventAsync(Key(ConsoleKey.G, 'g'));
        Assert.AreEqual(1, combo.SelectedIndex);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.G, 'g'));
        Assert.AreEqual(3, combo.SelectedIndex);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.G, 'g'));
        Assert.AreEqual(1, combo.SelectedIndex);
        Assert.IsFalse(await combo.HandleKeyEventAsync(Key(ConsoleKey.Z, 'z')));
        Assert.AreEqual(1, combo.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComboBox_Empty_IgnoresMovementAndTypeAhead()
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        ComboBox<string> combo = new(Theme()) { Position = new Position(2, 2), ViewSize = new Size(20, 1) };
        root.AddControl(combo);
        combo.SetFocus();
        await combo.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await combo.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.IsFalse(await combo.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
        Assert.IsNull(combo.SelectedItem);
        combo.Draw(new MockBuffer(new Size(80, 25)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComboBox_ClickOpens_WheelSteps_DisabledAndReadOnlyDoNothing()
    {
        (_, ComboBox<string> combo) = NewCombo();
        await combo.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 2), WheelDelta = -1 });
        Assert.AreEqual(1, combo.SelectedIndex);
        await combo.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 2), WheelDelta = 1 });
        Assert.AreEqual(0, combo.SelectedIndex);
        combo.IsEnabled = false;
        await combo.HandleMouseEventAsync(Click(5, 2));
        Assert.IsFalse(combo.IsOpen);
        combo.IsEnabled = true;
        combo.IsReadOnly = true;
        await combo.HandleMouseEventAsync(Click(5, 2));
        await combo.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.IsFalse(combo.IsOpen);
        combo.IsReadOnly = false;
        Assert.IsFalse(await combo.HandleMouseEventAsync(Click(5, 9)));
        await combo.HandleMouseEventAsync(Click(5, 2));
        Assert.IsTrue(combo.IsOpen);
        combo.Close();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ComboBox_Draw_ShowsTheSelectionAndTruncatesToTheWidth()
    {
        (_, ComboBox<string> combo) = NewCombo();
        combo.Label = "Colour";
        combo.ViewSize = new Size(14, 1);
        MockBuffer buffer = new(new Size(80, 25));
        combo.Draw(buffer);
        string line = Lines(buffer)[2];
        Assert.IsTrue(line.Contains("Colour"));
        Assert.IsTrue(line.Contains('▾'));
        combo.ViewSize = new Size(4, 1);
        combo.Draw(new MockBuffer(new Size(80, 25)));
    }

    // ---- BreakdownChart ----

    [Timeout(60_000)]
    [TestMethod]
    public void BreakdownChart_Allocate_AlwaysFillsTheWidth()
    {
        int[] widths = BreakdownChart.Allocate([1, 1, 1], 10);
        Assert.AreEqual(10, widths.Sum());
        CollectionAssert.AreEqual(new[] { 0, 0 }, BreakdownChart.Allocate([0, -3], 10));
        CollectionAssert.AreEqual(new[] { 0 }, BreakdownChart.Allocate([5], 0));
        CollectionAssert.AreEqual(new[] { 0, 10 }, BreakdownChart.Allocate([-1, 4], 10));
    }

    private static BreakdownChart NewBreakdown(int width, int height) => new(Theme())
    {
        Position = new Position(0, 0),
        ViewSize = new Size(width, height),
        Items = [new BreakdownChartItem("Used", 75, Color.Red), new BreakdownChartItem("Free", 25, Color.Green)],
    };

    [Timeout(60_000)]
    [TestMethod]
    public void BreakdownChart_Draw_ShowsTheBarAndALegendWithShares()
    {
        BreakdownChart chart = NewBreakdown(20, 4);
        MockBuffer buffer = new(new Size(20, 4));
        chart.Draw(buffer);
        string[] lines = Lines(buffer);
        Assert.AreEqual(20, lines[0].Count(c => c == '█'));
        Assert.IsTrue(lines[1].StartsWith("■ Used 75 (75%)"));
        Assert.IsTrue(lines[2].StartsWith("■ Free 25 (25%)"));
        Assert.AreEqual(string.Empty, lines[3].Trim());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BreakdownChart_Draw_WithoutPercentagesOrItems()
    {
        BreakdownChart chart = NewBreakdown(20, 3);
        chart.ShowPercentages = false;
        MockBuffer buffer = new(new Size(20, 3));
        chart.Draw(buffer);
        Assert.IsFalse(Lines(buffer)[1].Contains('%'));
        chart.Items = null!;
        MockBuffer empty = new(new Size(20, 3));
        chart.Draw(empty);
        Assert.AreEqual(string.Empty, Lines(empty)[0].Trim());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BreakdownChart_Draw_OnANarrowViewLeavesNoStaleLegendCell()
    {
        BreakdownChart chart = NewBreakdown(1, 3);
        MockBuffer buffer = new(new Size(1, 3));
        buffer.Write(0, new Position(0, 1), new Size(1, 1), new ConxelString("X", Color.White, Color.Black));
        chart.Draw(buffer);
        Assert.AreEqual(" ", Lines(buffer)[1]);
        BreakdownChart wide = NewBreakdown(8, 3);
        wide.Draw(new MockBuffer(new Size(8, 3)));
    }

    // ---- Gauge ----

    [Timeout(60_000)]
    [TestMethod]
    public void Gauge_Value_IsClampedAndNaNBecomesZero()
    {
        Gauge gauge = new(Theme());
        gauge.Value = 4;
        Assert.AreEqual(1.0, gauge.Value);
        gauge.Value = -2;
        Assert.AreEqual(0.0, gauge.Value);
        gauge.Value = double.NaN;
        Assert.AreEqual(0.0, gauge.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Gauge_SeverityColor_FollowsTheThresholds()
    {
        Assert.AreEqual(Color.Green, Gauge.SeverityColor(0.5));
        Assert.AreEqual(Color.Yellow, Gauge.SeverityColor(0.8));
        Assert.AreEqual(Color.Red, Gauge.SeverityColor(0.95));
        Assert.AreEqual(Color.Yellow, Gauge.SeverityColor(0.5, warning: 0.4, critical: 0.6));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Gauge_Draw_ShowsLabelBarAndPercent_AndFallsBackToTextWhenTooNarrow()
    {
        Gauge gauge = new(Theme()) { Label = "CPU", Value = 0.5, Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        MockBuffer buffer = new(new Size(20, 1));
        gauge.Draw(buffer);
        string line = Lines(buffer)[0];
        Assert.IsTrue(line.StartsWith("CPU "));
        Assert.IsTrue(line.TrimEnd().EndsWith("50%"));
        gauge.ViewSize = new Size(8, 1);
        MockBuffer narrow = new(new Size(8, 1));
        gauge.Draw(narrow);
        Assert.AreEqual("CPU  50%", Lines(narrow)[0]);
    }

    // ---- StatusDot ----

    [Timeout(60_000)]
    [TestMethod]
    public void StatusDot_Draw_ShowsTheDotAndTextCutToTheWidth()
    {
        StatusDot dot = new(Theme()) { Text = "Connected", Kind = BadgeKind.Success, Position = new Position(0, 0), ViewSize = new Size(8, 1) };
        MockBuffer buffer = new(new Size(8, 1));
        dot.Draw(buffer);
        Assert.AreEqual("● Connec", Lines(buffer)[0]);
        dot.ViewSize = new Size(2, 1);
        MockBuffer tiny = new(new Size(2, 1));
        dot.Draw(tiny);
        Assert.AreEqual("●", Lines(tiny)[0].Trim());
        dot.Text = null!;
        Assert.AreEqual(1, dot.NaturalWidth);
        dot.IsEnabled = false;
        dot.Draw(new MockBuffer(new Size(8, 1)));
    }

    // ---- StepIndicator / Alert ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task StepIndicator_ClickRaisesTheStepOnlyWhenEnabledAndOnAGlyph()
    {
        StepIndicator steps = new(Theme()) { Count = 4, Current = 1, Position = new Position(0, 0) };
        List<int> clicked = [];
        steps.StepClicked += (_, i) => clicked.Add(i);
        await steps.HandleMouseEventAsync(Click(4));
        await steps.HandleMouseEventAsync(Click(1));
        CollectionAssert.AreEqual(new[] { 2 }, clicked);
        steps.IsEnabled = false;
        await steps.HandleMouseEventAsync(Click(0));
        Assert.AreEqual(1, clicked.Count);
        Assert.AreEqual(-1, steps.StepAtColumn(-2));
        Assert.AreEqual(-1, steps.StepAtColumn(8));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Alert_CloseCrossDismissesOnlyWhenEnabled()
    {
        Alert alert = new(Theme()) { Text = "Saved", IsDismissible = true, Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        int dismissed = 0;
        alert.Dismissed += (_, _) => dismissed++;
        alert.IsEnabled = false;
        await alert.HandleMouseEventAsync(Click(19));
        Assert.AreEqual(0, dismissed);
        alert.IsEnabled = true;
        await alert.HandleMouseEventAsync(Click(10));
        Assert.AreEqual(0, dismissed);
        await alert.HandleMouseEventAsync(Click(19));
        Assert.AreEqual(1, dismissed);
        Assert.AreEqual(string.Empty, alert.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Alert_Enter_DismissesLikeEscape_WhenFocusedAndDismissible()
    {
        Alert alert = new(Theme()) { Text = "Saved", IsDismissible = true, Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        int dismissed = 0;
        alert.Dismissed += (_, _) => dismissed++;

        Assert.IsFalse(await alert.HandleKeyEventAsync(Key(ConsoleKey.Enter)), "not focused");
        Assert.AreEqual(0, dismissed);

        alert.SetFocus();
        Assert.IsTrue(await alert.HandleKeyEventAsync(Key(ConsoleKey.Enter)));
        Assert.AreEqual(1, dismissed);
        Assert.AreEqual(string.Empty, alert.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Alert_Enter_IsLeftAloneWhenNotDismissible()
    {
        Alert alert = new(Theme()) { Text = "Saved", Position = new Position(0, 0), ViewSize = new Size(20, 1) };

        Assert.IsFalse(await alert.HandleKeyEventAsync(Key(ConsoleKey.Enter)));

        Assert.AreEqual("Saved", alert.Text);
    }
}
