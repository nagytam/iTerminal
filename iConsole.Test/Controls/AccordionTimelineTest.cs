using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class AccordionTimelineTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    private static Accordion CreateAccordion()
    {
        Accordion accordion = new(Theme()) { ViewSize = new Size(30, 0) };
        accordion.AddSection("One", new Label("a", Theme()), 2);
        accordion.AddSection("Two", new Label("b", Theme()), 2);
        accordion.AddSection("Three", new Label("c", Theme()), 2);
        return accordion;
    }

    [TestMethod]
    public void Accordion_Collapsed_TakesOneRowPerSection()
    {
        Accordion accordion = CreateAccordion();
        Assert.AreEqual(3, (int)accordion.ViewSize.Height);
        Assert.AreEqual(-1, accordion.OpenIndex);
        Assert.AreEqual(2, (int)accordion.Sections[2].Position.Y);
    }

    [TestMethod]
    public void Accordion_OpeningOne_ClosesTheOthersAndReflows()
    {
        Accordion accordion = CreateAccordion();
        accordion.Sections[0].IsExpanded = true;
        accordion.Sections[1].IsExpanded = true;
        Assert.AreEqual(1, accordion.OpenIndex);
        Assert.IsFalse(accordion.Sections[0].IsExpanded);
        Assert.AreEqual(1, (int)accordion.Sections[1].Position.Y);
        Assert.AreEqual(4, (int)accordion.Sections[2].Position.Y);
        Assert.AreEqual(5, (int)accordion.ViewSize.Height);
    }

    [TestMethod]
    public void Accordion_OpenMinusOne_ClosesAll()
    {
        Accordion accordion = CreateAccordion();
        accordion.Open(2);
        accordion.Open(-1);
        Assert.AreEqual(-1, accordion.OpenIndex);
        Assert.AreEqual(3, (int)accordion.ViewSize.Height);
    }

    [TestMethod]
    public void Timeline_Draw_ShowsEventsJoinedByConnectors()
    {
        MockBuffer buffer = new(new Size(30, 3));
        Timeline timeline = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, 3) };
        timeline.SetEvents([new TimelineEvent("10:02", "Build"), new TimelineEvent("10:05", "Tests")]);
        timeline.Draw(buffer);
        Assert.AreEqual("● 10:02  Build", Lines(buffer)[0].TrimEnd());
        Assert.AreEqual("│", Lines(buffer)[1].TrimEnd());
        Assert.AreEqual("● 10:05  Tests", Lines(buffer)[2].TrimEnd());
    }

    [TestMethod]
    public void Timeline_FirstVisibleEvent_ShowsNewest()
    {
        Assert.AreEqual(0, Timeline.FirstVisibleEvent(2, 3));
        Assert.AreEqual(3, Timeline.FirstVisibleEvent(5, 3));
        Assert.AreEqual(0, Timeline.FirstVisibleEvent(0, 5));
    }

    [TestMethod]
    public void Timeline_Draw_BlanksRowsOfARemovedHistory()
    {
        MockBuffer buffer = new(new Size(30, 3));
        Timeline timeline = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, 3) };
        timeline.SetEvents([new TimelineEvent("1", "a"), new TimelineEvent("2", "b")]);
        timeline.Draw(buffer);
        timeline.SetEvents([]);
        timeline.Draw(buffer);
        Assert.AreEqual(string.Empty, Lines(buffer)[0].Trim());
    }

    [TestMethod]
    public async Task SearchBox_Enter_RaisesSubmittedOnlyWhenListened()
    {
        SearchBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme())
        {
            ViewSize = new Size(30, 1),
            Text = "abc",
        };
        box.SetFocus();
        KeyEvent enter = new('\r', ConsoleKey.Enter, false, false, false);
        Assert.IsFalse(await box.HandleKeyEventAsync(enter));

        string? submitted = null;
        box.Submitted += (_, text) => submitted = text;
        Assert.IsTrue(await box.HandleKeyEventAsync(enter));
        Assert.AreEqual("abc", submitted);
    }
}
