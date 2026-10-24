using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ControlRenderGapsTest
{
    private static TestThemeService Theme() => new();

    [TestMethod]
    public void SegmentedControl_KeepsTheSelectedOptionOnScreen()
    {
        SegmentedControl control = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(14, 1) };
        control.Options = ["Day", "Week", "Month", "Year"];
        control.SelectedIndex = 3;

        var screen = ScreenBuffer.Render(control, 14, 1);

        Assert.IsTrue(screen.Contains("Year"), "the chosen option must be visible: " + screen.Line(0));
    }

    [TestMethod]
    public void Countdown_KeepsTheTimeOnScreenWhenTheLabelIsLong()
    {
        var clock = new Mock<IDateTimeProviderService>();
        clock.SetupGet(c => c.Now).Returns(new DateTime(2026, 1, 1, 12, 0, 0));
        Countdown countdown = new(Theme(), clock.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(8, 1),
            Label = "Session expires in",
        };
        countdown.Start(TimeSpan.FromSeconds(90));

        var screen = ScreenBuffer.Render(countdown, 8, 1);

        Assert.IsTrue(screen.Contains("01:30"), "the time is the point of the control: " + screen.Line(0));
    }

    [TestMethod]
    public void KeyValueList_EmptyingTheListClearsTheRowsItPainted()
    {
        KeyValueList list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        list.SetItems([("Name", "iTerminal"), ("Version", "1.2.3")]);
        ScreenBuffer screen = new(20, 3);
        list.Draw(screen);

        list.SetItems([]);
        list.Draw(screen);

        Assert.IsFalse(screen.Contains("Version"), "a list that lost its pairs keeps showing them: " + screen.Line(1));
    }

    [TestMethod]
    public void Accordion_SectionsFollowTheWidthOfTheAccordion()
    {
        Accordion accordion = new(Theme());
        Expander section = accordion.AddSection("Advanced", new KeyValueList(Theme()));

        accordion.ViewSize = new Size(25, accordion.ViewSize.Height);
        accordion.OnIdle();

        Assert.AreEqual(25, section.ViewSize.Width);
    }

    [TestMethod]
    public void SliderAndSegmentedControl_AdvertiseTheUpAndDownKeysTheyAlreadyHandle()
    {
        Assert.IsTrue(new Slider(Theme()).GetKeyHints().Any(h => h.Key == "Up/Down"));
        Assert.IsTrue(new SegmentedControl(Theme()).GetKeyHints().Any(h => h.Key == "Up/Down"));
    }
}
