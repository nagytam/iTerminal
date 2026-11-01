using iConsole.Controls;
using iConsole.Controls.Charts;
using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Status bar layout, the clock controls, the line chart, and clicks on disabled tables and trees.</summary>
[TestClass]
public class StatusChartsAndClockInteractionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    private sealed class FakeClock : IDateTimeProviderService
    {
        public DateTime Time { get; set; } = new(2026, 10, 18, 12, 0, 0);

        public DateTime Now => Time;

        public DateTime UtcNow => Time.ToUniversalTime();
    }

    // ---- ElapsedClock ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task ElapsedClock_Click_FocusesAndTogglesRunning()
    {
        ElapsedClock watch = new(Theme(), new FakeClock()) { Position = new Position(0, 0) };
        await watch.HandleMouseEventAsync(Click(2));
        Assert.IsTrue(watch.IsFocused);
        Assert.IsTrue(watch.IsRunning);
        await watch.HandleMouseEventAsync(Click(2));
        Assert.IsFalse(watch.IsRunning);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ElapsedClock_DisabledOrClickedOutside_DoesNothing()
    {
        ElapsedClock watch = new(Theme(), new FakeClock()) { Position = new Position(0, 0) };
        Assert.IsFalse(await watch.HandleMouseEventAsync(Click(50)));
        watch.IsEnabled = false;
        await watch.HandleMouseEventAsync(Click(2));
        Assert.IsFalse(watch.IsRunning);
        Assert.IsFalse(watch.IsFocused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ElapsedClock_Draw_ShowsLabelAndTimeAndFollowsTheClockOnIdle()
    {
        FakeClock clock = new();
        ElapsedClock watch = new(Theme(), clock) { Label = "Up", Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        watch.Start();
        clock.Time = clock.Time.AddSeconds(75);
        watch.OnIdle();
        MockBuffer buffer = new(new Size(20, 1));
        watch.Draw(buffer);
        Assert.AreEqual("Up 01:15", buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd());
        watch.Stop();
        watch.Stop();
        watch.Start();
        watch.Start();
        Assert.IsTrue(watch.IsRunning);
    }

    // ---- Date ----

    [Timeout(60_000)]
    [TestMethod]
    public void Date_ShowsTheProvidersTimeAndFollowsItOnIdle()
    {
        FakeClock clock = new();
        using Date date = new(Theme(), clock);
        Assert.AreEqual("2026-10-18 12:00:00", date.Text);
        clock.Time = clock.Time.AddSeconds(5);
        date.OnIdle();
        Assert.AreEqual("2026-10-18 12:00:05", date.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Date_StartAndDispose_AreSafeToRepeat()
    {
        Date date = new(Theme(), new FakeClock());
        await date.OnInitializeAsync();
        date.Start();
        date.Dispose();
        date.Dispose();
        date.Start();
    }

    // ---- StatusBar layout ----

    private sealed class TestStatusBar(IThemeService theme) : iConsole.Controls.StatusBar.StatusBar(theme)
    {
        public void Fit(IReadOnlyList<Control> inOrder, IReadOnlyList<Control> byPriority) => StackFitting(inOrder, byPriority);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StatusBar_StackFitting_KeepsTheMostImportantItemsThatFit()
    {
        TestStatusBar bar = new(Theme()) { ViewSize = new Size(25, 1) };
        Window parent = new(Theme()) { ViewSize = new Size(80, 5) };
        parent.AddControl(bar);
        Label a = new("aaaaaaaaaa", Theme()) { ViewSize = new Size(10, 1) };
        Label b = new("bbbbbbbbbb", Theme()) { ViewSize = new Size(10, 1) };
        Label c = new("cccccccccc", Theme()) { ViewSize = new Size(10, 1) };
        bar.AddControl(a);
        bar.AddControl(b);
        bar.AddControl(c);

        bar.Fit([a, b, c], [c, a, b]);

        Assert.AreEqual(0, (int)a.Position.X);
        Assert.AreEqual(10, (int)c.Position.X);
        Assert.IsFalse(b.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StatusBar_ShowAsync_KeepsLeftOutItemsHidden()
    {
        TestStatusBar bar = new(Theme()) { ViewSize = new Size(12, 1) };
        Window parent = new(Theme()) { ViewSize = new Size(80, 5) };
        parent.AddControl(bar);
        Label a = new("aaaaaaaaaa", Theme()) { ViewSize = new Size(10, 1) };
        Label b = new("bbbbbbbbbb", Theme()) { ViewSize = new Size(10, 1) };
        bar.AddControl(a);
        bar.AddControl(b);
        bar.Fit([a, b], [a, b]);

        await bar.ShowAsync();

        Assert.IsFalse(b.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StatusBar_Layout_FollowsTheParentWidth()
    {
        iConsole.Controls.StatusBar.StatusBar bar = new(Theme());
        Window parent = new(Theme()) { ViewSize = new Size(60, 5) };
        parent.AddControl(bar);
        bar.OnInitializeLayout();
        Assert.AreEqual(60, (int)bar.ViewSize.Width);
        Assert.AreEqual(1, (int)bar.ViewSize.Height);
        Assert.IsFalse(bar.CanFocus);
        bar.Draw(new MockBuffer(new Size(60, 5)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ViewerStatusBar_Layout_IsOneRowHigh()
    {
        Window parent = new(Theme()) { ViewSize = new Size(100, 10) };
        ViewerStatusBar bar = new(
            Mock.Of<IScrollableModel>(),
            Mock.Of<ISearchModel>(),
            Mock.Of<ITextModel>(),
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            Theme());
        parent.AddControl(bar);
        bar.OnInitializeLayout();
        Assert.AreEqual(1, (int)bar.ViewSize.Height);
        Assert.AreEqual(nameof(ViewerStatusBar), bar.Name);
        Assert.IsTrue(bar.Controls.Count >= 6);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ViewerStatusBar_Layout_OnANarrowParentHidesWhatDoesNotFit()
    {
        Window parent = new(Theme()) { ViewSize = new Size(40, 10) };
        ViewerStatusBar bar = new(
            Mock.Of<IScrollableModel>(),
            Mock.Of<ISearchModel>(),
            Mock.Of<ITextModel>(),
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            Theme());
        parent.AddControl(bar);
        bar.OnInitializeLayout();
        long used = bar.Controls.Where(c => c.IsVisible).Sum(c => (long)c.ViewSize.Width);
        Assert.IsTrue(used <= bar.ViewSize.Width);
    }

    // ---- LineChart ----

    [Timeout(60_000)]
    [TestMethod]
    public void LineChart_Render_HasTheRequestedShape()
    {
        string[] rows = LineChart.Render([1, 2, 3, 4], 6, 2);
        Assert.AreEqual(2, rows.Length);
        Assert.IsTrue(rows.All(r => r.Length == 6));
        Assert.IsTrue(rows.All(r => r.All(c => c is >= '⠀' and <= '⣿')));
        Assert.AreEqual(0, LineChart.Render([1, 2], 0, 2).Length);
        Assert.AreEqual(0, LineChart.Render([1, 2], 4, 0).Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LineChart_Render_EmptyAndNaNSeriesDrawNoDots()
    {
        string blank = new('⠀', 4);
        CollectionAssert.AreEqual(new[] { blank }, LineChart.Render([], 4, 1));
        CollectionAssert.AreEqual(new[] { blank }, LineChart.Render([double.NaN, double.NaN], 4, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LineChart_Render_RisingSeriesClimbsFromBottomLeftToTopRight()
    {
        string[] rows = LineChart.Render([0, 1], 4, 2);
        Assert.AreNotEqual('⠀', rows[1][0]);
        Assert.AreNotEqual('⠀', rows[0][3]);
        Assert.AreEqual('⠀', rows[0][0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LineChart_Render_FlatSeriesStaysOnOneDotRow_AndBoundsClampValues()
    {
        string[] flat = LineChart.Render([5, 5, 5], 4, 1);
        Assert.AreEqual(1, flat.Length);
        string[] clamped = LineChart.Render([100, 100], 2, 1, minimum: 0, maximum: 10);
        string[] top = LineChart.Render([10, 10], 2, 1, minimum: 0, maximum: 10);
        CollectionAssert.AreEqual(top, clamped);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LineChart_Draw_WritesEveryRow_AndHonoursTheForegroundOverride()
    {
        LineChart chart = new(Theme())
        {
            Values = [1, 3, 2, 5],
            Foreground = Color.Red,
            Position = new Position(0, 0),
            ViewSize = new Size(8, 3),
        };
        MockBuffer buffer = new(new Size(8, 3));
        chart.Draw(buffer);
        string[] lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines.Take(3).All(l => l.Length >= 8));
        Assert.IsTrue(lines.Take(3).Any(l => l.Any(c => c > '⠀')));
        chart.Values = null!;
        Assert.AreEqual(0, chart.Values.Count);
        chart.Draw(new MockBuffer(new Size(8, 3)));
    }

    // ---- Disabled Table / TreeView ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task Table_Disabled_ClickDoesNotMoveCursorOrSort()
    {
        Table<string> table = new(new ConcurrentObservableCollection<string>(["a", "b", "c"]), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 5),
        };
        table.Columns.Add(new ColumnDefinition<string>("Name", 10, s => s));
        table.OnInitializeLayout();
        int sorts = 0;
        table.SortRequested += (_, _) => sorts++;
        table.IsEnabled = false;
        await table.HandleMouseEventAsync(Click(2, 0));
        await table.HandleMouseEventAsync(Click(2, 3));
        Assert.AreEqual(0, sorts);
        Assert.AreEqual(0, table.CursorRow);
        table.IsEnabled = true;
        await table.HandleMouseEventAsync(Click(2, 3));
        Assert.AreEqual(2, table.CursorRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TreeView_Disabled_ClickDoesNotMoveFocus()
    {
        HierarchicalItemModel root = new(null, "/") { IsOpened = true };
        HierarchicalItemModel a = new(root, "A");
        HierarchicalItemModel b = new(root, "B");
        root.Children.Add(a);
        root.Children.Add(b);
        root.IsFocused = true;
        TreeView tree = new(root, Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 5) };
        tree.IsEnabled = false;
        await tree.HandleMouseEventAsync(Click(3, 2));
        Assert.IsFalse(b.IsFocused);
        tree.IsEnabled = true;
        await tree.HandleMouseEventAsync(Click(3, 2));
        Assert.IsTrue(b.IsFocused);
    }
}
