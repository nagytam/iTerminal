using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.StatusBar;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Settings that took null or a huge number and failed every later draw (found by <see cref="ControlPropertySweepTest"/>).</summary>
[TestClass]
public class ControlsReviewBatch29Test
{
    private readonly TestThemeService _theme = new();

    private static IBeepService Beep => Mock.Of<IBeepService>();

    [Timeout(60_000)]
    [TestMethod]
    public void Button_NullTitle_IsEmpty_AndDraws()
    {
        Button button = new(string.Empty, Mock.Of<iConsole.Commands.ICommand>(), _theme) { Position = new Position(0, 0), ViewSize = new Size(10, 1) };

        button.Title = null!;

        Assert.AreEqual(string.Empty, button.Title);
        ScreenBuffer.Render(button, 10, 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_NullTextAndPlaceholder_AreEmpty_AndDraw()
    {
        TextBox box = new(Beep, new DateTimeProviderService(), _theme) { Position = new Position(0, 0), ViewSize = new Size(10, 1) };

        box.Text = null!;
        box.Placeholder = null!;

        Assert.AreEqual(string.Empty, box.Text);
        Assert.AreEqual(string.Empty, box.Placeholder);
        ScreenBuffer.Render(box, 10, 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleButton_NullLabels_AreEmpty_AndDraw()
    {
        ToggleButton toggle = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(10, 1), LabelOn = null!, LabelOff = null! };

        Assert.AreEqual(string.Empty, toggle.LabelOn);
        Assert.AreEqual(string.Empty, toggle.LabelOff);
        ScreenBuffer.Render(toggle, 10, 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ElapsedClock_NullLabel_IsEmpty_AndDraws()
    {
        ElapsedClock clock = new(_theme, new DateTimeProviderService()) { Position = new Position(0, 0), ViewSize = new Size(12, 1), Label = null! };

        Assert.AreEqual(string.Empty, clock.Label);
        ScreenBuffer.Render(clock, 12, 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CheckList_NullEmptyText_IsEmpty_AndDraws()
    {
        CheckList list = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(12, 2), EmptyText = null! };

        Assert.AreEqual(string.Empty, list.EmptyText);
        ScreenBuffer.Render(list, 12, 2);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StatusBarItem_NullText_IsEmpty_AndDraws()
    {
        Date item = new(_theme, new DateTimeProviderService()) { Position = new Position(0, 0), ViewSize = new Size(12, 1), Text = null! };

        Assert.AreEqual(string.Empty, item.Text);
        ScreenBuffer.Render(item, 12, 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Rating_HugeMaximum_IsCapped()
    {
        Rating rating = new(_theme) { Maximum = int.MaxValue };

        Assert.AreEqual(Rating.MaxStars, rating.Maximum);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StepIndicator_HugeCount_IsCapped()
    {
        StepIndicator steps = new(_theme) { Count = int.MaxValue };

        Assert.AreEqual(StepIndicator.MaxSteps, steps.Count);
        Assert.AreEqual(StepIndicator.WidthFor(StepIndicator.MaxSteps), (int)steps.ViewSize.Width);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Tab_NullPages_MeansNoPages_AndDraws()
    {
        Tab tab = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };

        tab.TabPages = null!;

        Assert.AreEqual(0, tab.TabPages.Count);
        ScreenBuffer.Render(tab, 20, 3);
    }
}
