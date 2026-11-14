using Moq;
using iTerminal.Core.Model.ToDo;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Model.ToDo;

[TestClass]
public class ToDoTaskTest
{
    private static readonly DateTime Now = new(2026, 11, 14, 10, 0, 0);

    [Timeout(60_000)]
    [TestMethod]
    public void ASubjectAloneIsANormalOpenTaskWithNoDueDate()
    {
        Assert.IsTrue(ToDoTask.TryParse("Buy milk", Guid.Empty, Now, out var task, out var error));

        Assert.AreEqual(string.Empty, error);
        Assert.AreEqual("Buy milk", task!.Subject);
        Assert.IsNull(task.DueDate);
        Assert.AreEqual(ToDoPriority.Normal, task.Priority);
        Assert.IsFalse(task.IsComplete);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ABangMakesItHighPriorityAndATrailingDateSetsTheDueDate()
    {
        Assert.IsTrue(ToDoTask.TryParse("!Renew passport @2026-11-01", Guid.Empty, Now, out var task, out _));

        Assert.AreEqual("Renew passport", task!.Subject);
        Assert.AreEqual(ToDoPriority.High, task.Priority);
        Assert.AreEqual(new DateOnly(2026, 11, 1), task.DueDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("")]
    [DataRow("!")]
    [DataRow("@2026-11-01")]
    [DataRow("Pay rent @next-month")]
    [DataRow("Pay rent @2026-13-01")]
    public void AnEmptyOrBadlyDatedTaskIsRefused(string text)
    {
        Assert.IsFalse(ToDoTask.TryParse(text, Guid.Empty, Now, out var task, out var error));
        Assert.IsNull(task);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PriorityCyclesNormalHighLowNormal()
    {
        Assert.AreEqual(ToDoPriority.High, ToDoTask.NextPriority(ToDoPriority.Normal));
        Assert.AreEqual(ToDoPriority.Low, ToDoTask.NextPriority(ToDoPriority.High));
        Assert.AreEqual(ToDoPriority.Normal, ToDoTask.NextPriority(ToDoPriority.Low));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ATimeOfDayIsTodayWhenItIsStillAheadAndTomorrowWhenItHasPassed()
    {
        Assert.IsTrue(ToDoTask.TryParse("Call the bank ~15:30", Guid.Empty, Now, out var later, out _));
        Assert.AreEqual(new DateTime(2026, 11, 14, 15, 30, 0), later!.RemindAt);

        Assert.IsTrue(ToDoTask.TryParse("Call the bank ~09:00", Guid.Empty, Now, out var tomorrow, out _));
        Assert.AreEqual(new DateTime(2026, 11, 15, 9, 0, 0), tomorrow!.RemindAt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFullTimestampIsTheExactReminderTime()
    {
        Assert.IsTrue(ToDoTask.TryParse("Pay rent @2026-11-01 ~2026-11-20T08:15", Guid.Empty, Now, out var task, out _));

        Assert.AreEqual("Pay rent", task!.Subject);
        Assert.AreEqual(new DateOnly(2026, 11, 1), task.DueDate);
        Assert.AreEqual(new DateTime(2026, 11, 20, 8, 15, 0), task.RemindAt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDueDateAndTheReminderMayComeInEitherOrder()
    {
        Assert.IsTrue(ToDoTask.TryParse("Pay rent ~09:00 @2026-11-01", Guid.Empty, Now, out var task, out _));

        Assert.AreEqual("Pay rent", task!.Subject);
        Assert.AreEqual(new DateOnly(2026, 11, 1), task.DueDate);
        Assert.AreEqual(new DateTime(2026, 11, 15, 9, 0, 0), task.RemindAt);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("Pay rent ~2026-11-14T09:00")]
    [DataRow("Pay rent ~noon")]
    [DataRow("Pay rent ~")]
    [DataRow("Pay rent ~09:00 ~10:00")]
    [DataRow("Pay rent @2026-11-01 @2026-11-02")]
    public void AnUnusableReminderIsRefused(string text)
    {
        Assert.IsFalse(ToDoTask.TryParse(text, Guid.Empty, Now, out var task, out var error));
        Assert.IsNull(task);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyReminderTextClearsTheReminder()
    {
        Assert.IsTrue(ToDoTask.TryParseReminder("   ", Now, out var remindAt, out var error));

        Assert.IsNull(remindAt);
        Assert.AreEqual(string.Empty, error);
    }
}

public class ToDoStoreTest
{
    private static readonly DateTime Now = new(2026, 11, 14, 10, 0, 0);

    private static ToDoTask Task(string subject, DateOnly? due = null, ToDoPriority priority = ToDoPriority.Normal, bool done = false) =>
        new(Guid.NewGuid(), subject, due, priority, done);

    private static ToDoStore NewStore(out Mock<ISettingsService> settings)
    {
        settings = new Mock<ISettingsService>();
        return new ToDoStore(settings.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OpenTasksComeBeforeDoneOnesAndEarlierDueDatesFirst()
    {
        var store = NewStore(out _);
        store.Add(Task("Done early", new DateOnly(2026, 1, 1), done: true));
        store.Add(Task("No date"));
        store.Add(Task("Due later", new DateOnly(2026, 12, 1)));
        store.Add(Task("Due sooner", new DateOnly(2026, 10, 1)));

        CollectionAssert.AreEqual(
            new[] { "Due sooner", "Due later", "No date", "Done early" },
            store.Visible(showCompleted: true).Select(t => t.Subject).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HighPriorityComesFirstWhenDatesAreEqual()
    {
        var store = NewStore(out _);
        store.Add(Task("Low one", priority: ToDoPriority.Low));
        store.Add(Task("High one", priority: ToDoPriority.High));

        CollectionAssert.AreEqual(new[] { "High one", "Low one" }, store.Visible(true).Select(t => t.Subject).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HidingDoneTasksLeavesThemOutOfTheList()
    {
        var store = NewStore(out _);
        store.Add(Task("Open"));
        store.Add(Task("Finished", done: true));

        Assert.AreEqual(1, store.Visible(showCompleted: false).Count);
        Assert.AreEqual(2, store.Visible(showCompleted: true).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UpdateReplacesTheTaskWithTheSameId()
    {
        var store = NewStore(out _);
        var task = Task("Write report");
        store.Add(task);

        store.Update(task with { IsComplete = true });

        Assert.IsTrue(store.Visible(true).Single().IsComplete);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TasksSurviveAStoreBeingReopened()
    {
        string? saved = null;
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Set(ToDoStore.SettingsKey, It.IsAny<string>())).Callback<string, string>((_, v) => saved = v);
        settings.Setup(s => s.Get(ToDoStore.SettingsKey)).Returns(() => saved);

        new ToDoStore(settings.Object).Add(Task("Keep me", new DateOnly(2026, 11, 1), ToDoPriority.High));

        var reopened = new ToDoStore(settings.Object).Visible(true).Single();

        Assert.AreEqual("Keep me", reopened.Subject);
        Assert.AreEqual(new DateOnly(2026, 11, 1), reopened.DueDate);
        Assert.AreEqual(ToDoPriority.High, reopened.Priority);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADamagedSettingDoesNotStopTheListOpening()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(ToDoStore.SettingsKey)).Returns("[oops");

        Assert.AreEqual(0, new ToDoStore(settings.Object).Visible(true).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TakeDueRemindersGivesEachOpenReminderOnceAndLeavesTheRest()
    {
        var store = new ToDoStore(new Mock<ISettingsService>().Object);
        var due = new ToDoTask(Guid.NewGuid(), "Due", null, ToDoPriority.Normal, false, Now.AddMinutes(-1));
        var later = new ToDoTask(Guid.NewGuid(), "Later", null, ToDoPriority.Normal, false, Now.AddHours(1));
        var done = new ToDoTask(Guid.NewGuid(), "Done", null, ToDoPriority.Normal, true, Now.AddMinutes(-1));
        store.Add(due);
        store.Add(later);
        store.Add(done);

        var given = store.TakeDueReminders(Now);

        Assert.AreEqual(1, given.Count);
        Assert.AreEqual("Due", given[0].Subject);
        Assert.IsNull(store.Visible(true).Single(t => t.Id == due.Id).RemindAt);
        Assert.AreEqual(later.RemindAt, store.Visible(true).Single(t => t.Id == later.Id).RemindAt);
        Assert.AreEqual(0, store.TakeDueReminders(Now).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AReminderGivenBeforeARestartIsNotGivenAgainAfterIt()
    {
        var settings = new Mock<ISettingsService>();
        string? saved = null;
        settings.Setup(s => s.Set(ToDoStore.SettingsKey, It.IsAny<string>())).Callback<string, string>((_, value) => saved = value);
        settings.Setup(s => s.Get(ToDoStore.SettingsKey)).Returns(() => saved);
        var store = new ToDoStore(settings.Object);
        store.Add(new ToDoTask(Guid.NewGuid(), "Due", null, ToDoPriority.Normal, false, Now.AddMinutes(-1)));
        Assert.AreEqual(1, store.TakeDueReminders(Now).Count);

        Assert.AreEqual(0, new ToDoStore(settings.Object).TakeDueReminders(Now).Count);
    }
}
