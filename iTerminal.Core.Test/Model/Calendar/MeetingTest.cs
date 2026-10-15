using Moq;
using iTerminal.Core.Model.Calendar;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Model.Calendar;

[TestClass]
public class MeetingTest
{
    private static readonly DateOnly Day = new(2026, 10, 14);

    [TestMethod]
    public void AStartAndATitleMakeAnHourLongMeeting()
    {
        Assert.IsTrue(Meeting.TryParse("14:30 Standup", Day, out var meeting, out var error));

        Assert.AreEqual(string.Empty, error);
        Assert.AreEqual(new Meeting(Day, new TimeOnly(14, 30), 60, "Standup"), meeting);
    }

    [TestMethod]
    public void ANumberAfterTheTimeIsTheDurationInMinutes()
    {
        Assert.IsTrue(Meeting.TryParse("9:05 45 Design review", Day, out var meeting, out _));

        Assert.AreEqual(new TimeOnly(9, 5), meeting!.Start);
        Assert.AreEqual(45, meeting.DurationMinutes);
        Assert.AreEqual("Design review", meeting.Title);
    }

    [TestMethod]
    public void TheDescriptionShowsTheWholeSlot()
    {
        var meeting = new Meeting(Day, new TimeOnly(14, 30), 45, "Standup");

        Assert.AreEqual("14:30–15:15  Standup", meeting.Describe());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("14:30")]
    [DataRow("25:00 Standup")]
    [DataRow("teatime Standup")]
    [DataRow("23:30 90 Late")]
    public void AnIncompleteOrImpossibleMeetingIsRefusedWithAReason(string text)
    {
        var accepted = Meeting.TryParse(text, Day, out var meeting, out var error);

        Assert.IsFalse(accepted);
        Assert.IsNull(meeting);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void AMeetingThatWouldRunPastMidnightIsRefused()
    {
        Assert.IsFalse(Meeting.TryParse("23:30 90 Late", Day, out _, out var error));

        Assert.AreEqual("A meeting has to end on the same day", error);
    }

    [TestMethod]
    public void TheStoreKeepsMeetingsAcrossInstances()
    {
        string? saved = null;
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Set(MeetingStore.SettingsKey, It.IsAny<string>())).Callback<string, string>((_, value) => saved = value);
        settings.Setup(s => s.Get(MeetingStore.SettingsKey)).Returns(() => saved);

        var first = new MeetingStore(settings.Object);
        first.Add(new Meeting(Day, new TimeOnly(15, 0), 30, "Later"));
        first.Add(new Meeting(Day, new TimeOnly(9, 0), 60, "Earlier"));

        var second = new MeetingStore(settings.Object);
        var meetings = second.On(Day);

        CollectionAssert.AreEqual(new[] { "Earlier", "Later" }, meetings.Select(m => m.Title).ToArray());
        settings.Verify(s => s.Write(), Times.AtLeastOnce);
    }

    [TestMethod]
    public void ADamagedSettingDoesNotStopTheCalendarOpening()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(MeetingStore.SettingsKey)).Returns("{not json");

        var store = new MeetingStore(settings.Object);

        Assert.AreEqual(0, store.On(Day).Count);
    }

    [TestMethod]
    public void RemovingAMeetingForgetsIt()
    {
        var settings = new Mock<ISettingsService>();
        var store = new MeetingStore(settings.Object);
        var meeting = new Meeting(Day, new TimeOnly(9, 0), 60, "Gone");
        store.Add(meeting);

        store.Remove(meeting);

        Assert.AreEqual(0, store.On(Day).Count);
    }
}
