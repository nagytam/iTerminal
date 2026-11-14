using System.Text.Json;
using iTerminal.Core.Model.Calendar;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Model.Calendar;

[TestClass]
public class MeetingStoreIdentityTest
{
    private static readonly DateOnly Day = new(2026, 10, 14);

    private static Mock<ISettingsService> Settings(string? json = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(MeetingStore.SettingsKey)).Returns(json);
        return settings;
    }

    /// <summary>A list as the app saved it before meetings had identities: the Uid is null in the JSON.</summary>
    private static string ListWithoutIdentities(params Meeting[] meetings) => JsonSerializer.Serialize(meetings);

    [Timeout(60_000)]
    [TestMethod]
    public void AddGivesTheMeetingAnIdentityAndReturnsTheStoredCopy()
    {
        var store = new MeetingStore(Settings().Object);

        var stored = store.Add(new Meeting(Day, new TimeOnly(9, 0), 30, "Standup"));

        Assert.IsFalse(string.IsNullOrEmpty(stored.Uid));
        Assert.AreEqual(stored.Uid, store.On(Day).Single().Uid);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemoveWithAMeetingNeverStoredRemovesNothing()
    {
        var store = new MeetingStore(Settings().Object);
        store.Add(new Meeting(Day, new TimeOnly(9, 0), 30, "Keep"));

        store.Remove(new Meeting(Day, new TimeOnly(9, 0), 30, "Keep"));

        Assert.AreEqual(1, store.On(Day).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemoveTakesOnlyTheMeetingWithTheSameIdentity()
    {
        var store = new MeetingStore(Settings().Object);
        var first = store.Add(new Meeting(Day, new TimeOnly(9, 0), 30, "Same"));
        var second = store.Add(new Meeting(Day, new TimeOnly(9, 0), 30, "Same"));

        store.Remove(first);

        Assert.AreEqual(second.Uid, store.On(Day).Single().Uid);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReplaceKeepsTheIdentityAndSavesOnce()
    {
        var settings = Settings();
        var store = new MeetingStore(settings.Object);
        var stored = store.Add(new Meeting(Day, new TimeOnly(9, 0), 30, "Standup"));

        var replaced = store.Replace(stored, new Meeting(Day, new TimeOnly(10, 0), 45, "Retro"));

        Assert.AreEqual(stored.Uid, replaced.Uid);
        var only = store.On(Day).Single();
        Assert.AreEqual("Retro", only.Title);
        Assert.AreEqual(stored.Uid, only.Uid);
        settings.Verify(s => s.Write(), Times.Exactly(2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReplaceOfAMeetingThatIsNotStoredAddsIt()
    {
        var store = new MeetingStore(Settings().Object);

        var added = store.Replace(new Meeting(Day, new TimeOnly(9, 0), 30, "Not stored"), new Meeting(Day, new TimeOnly(11, 0), 30, "New"));

        Assert.IsFalse(string.IsNullOrEmpty(added.Uid));
        Assert.AreEqual("New", store.On(Day).Single().Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LoadingAMeetingWithoutAnIdentityGivesOneWithoutWriting()
    {
        var settings = Settings(ListWithoutIdentities(new Meeting(Day, new TimeOnly(9, 0), 30, "Old")));
        var store = new MeetingStore(settings.Object);

        var loaded = store.On(Day).Single();

        Assert.IsFalse(string.IsNullOrEmpty(loaded.Uid));
        Assert.IsTrue(store.HasPendingIdentities);
        settings.Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        settings.Verify(s => s.Write(), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PersistingPendingIdentitiesWritesOnceAndThenNothing()
    {
        var settings = Settings(ListWithoutIdentities(new Meeting(Day, new TimeOnly(9, 0), 30, "Old")));
        var store = new MeetingStore(settings.Object);
        store.On(Day);

        store.PersistPendingIdentities();
        store.PersistPendingIdentities();

        Assert.IsFalse(store.HasPendingIdentities);
        settings.Verify(s => s.Write(), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheIdentityGivenAtLoadIsTheSameAfterARestart()
    {
        var saved = string.Empty;
        var settings = Settings(ListWithoutIdentities(new Meeting(Day, new TimeOnly(9, 0), 30, "Old")));
        settings.Setup(s => s.Set(MeetingStore.SettingsKey, It.IsAny<string>())).Callback<string, string>((_, value) => saved = value);
        var first = new MeetingStore(settings.Object);
        var identity = first.On(Day).Single().Uid;
        first.PersistPendingIdentities();

        var afterRestart = new MeetingStore(Settings(saved).Object);

        Assert.AreEqual(identity, afterRestart.On(Day).Single().Uid);
        Assert.IsFalse(afterRestart.HasPendingIdentities);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFailedWriteKeepsTheIdentitiesPendingForANewTry()
    {
        var failing = true;
        var settings = Settings(ListWithoutIdentities(new Meeting(Day, new TimeOnly(9, 0), 30, "Old")));
        settings.Setup(s => s.Write()).Callback(() =>
        {
            if (failing)
            {
                throw new IOException("disk full");
            }
        });
        var store = new MeetingStore(settings.Object);
        store.On(Day);

        Assert.ThrowsExactly<IOException>(store.PersistPendingIdentities);
        Assert.IsTrue(store.HasPendingIdentities);

        failing = false;
        store.PersistPendingIdentities();

        Assert.IsFalse(store.HasPendingIdentities);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadingTheCalendarDoesNotWaitForASaveInProgress()
    {
        var writing = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var settings = Settings(ListWithoutIdentities(new Meeting(Day, new TimeOnly(9, 0), 30, "Old")));
        settings.Setup(s => s.Write()).Callback(() =>
        {
            writing.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });
        var store = new MeetingStore(settings.Object);
        store.On(Day);

        var persist = Task.Run(() => store.PersistPendingIdentities());
        Assert.IsTrue(writing.Wait(TimeSpan.FromSeconds(10)), "the save never started");

        var read = Task.Run(() => store.On(Day).Count);
        var finishedWhileSaving = read.Wait(TimeSpan.FromSeconds(2));
        release.Set();
        persist.Wait();

        Assert.IsTrue(finishedWhileSaving, "reading the calendar waited for the save");
        Assert.AreEqual(1, read.Result);
    }
}
