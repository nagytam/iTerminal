using Moq;
using iTerminal.Core.Model.ToDo;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Model.ToDo;

[TestClass]
public class ToDoTaskTest
{
    [TestMethod]
    public void ASubjectAloneIsANormalOpenTaskWithNoDueDate()
    {
        Assert.IsTrue(ToDoTask.TryParse("Buy milk", Guid.Empty, out var task, out var error));

        Assert.AreEqual(string.Empty, error);
        Assert.AreEqual("Buy milk", task!.Subject);
        Assert.IsNull(task.DueDate);
        Assert.AreEqual(ToDoPriority.Normal, task.Priority);
        Assert.IsFalse(task.IsComplete);
    }

    [TestMethod]
    public void ABangMakesItHighPriorityAndATrailingDateSetsTheDueDate()
    {
        Assert.IsTrue(ToDoTask.TryParse("!Renew passport @2026-11-01", Guid.Empty, out var task, out _));

        Assert.AreEqual("Renew passport", task!.Subject);
        Assert.AreEqual(ToDoPriority.High, task.Priority);
        Assert.AreEqual(new DateOnly(2026, 11, 1), task.DueDate);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("!")]
    [DataRow("@2026-11-01")]
    [DataRow("Pay rent @next-month")]
    [DataRow("Pay rent @2026-13-01")]
    public void AnEmptyOrBadlyDatedTaskIsRefused(string text)
    {
        Assert.IsFalse(ToDoTask.TryParse(text, Guid.Empty, out var task, out var error));
        Assert.IsNull(task);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void PriorityCyclesNormalHighLowNormal()
    {
        Assert.AreEqual(ToDoPriority.High, ToDoTask.NextPriority(ToDoPriority.Normal));
        Assert.AreEqual(ToDoPriority.Low, ToDoTask.NextPriority(ToDoPriority.High));
        Assert.AreEqual(ToDoPriority.Normal, ToDoTask.NextPriority(ToDoPriority.Low));
    }
}

[TestClass]
public class ToDoStoreTest
{
    private static ToDoTask Task(string subject, DateOnly? due = null, ToDoPriority priority = ToDoPriority.Normal, bool done = false) =>
        new(Guid.NewGuid(), subject, due, priority, done);

    private static ToDoStore NewStore(out Mock<ISettingsService> settings)
    {
        settings = new Mock<ISettingsService>();
        return new ToDoStore(settings.Object);
    }

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

    [TestMethod]
    public void HighPriorityComesFirstWhenDatesAreEqual()
    {
        var store = NewStore(out _);
        store.Add(Task("Low one", priority: ToDoPriority.Low));
        store.Add(Task("High one", priority: ToDoPriority.High));

        CollectionAssert.AreEqual(new[] { "High one", "Low one" }, store.Visible(true).Select(t => t.Subject).ToArray());
    }

    [TestMethod]
    public void HidingDoneTasksLeavesThemOutOfTheList()
    {
        var store = NewStore(out _);
        store.Add(Task("Open"));
        store.Add(Task("Finished", done: true));

        Assert.AreEqual(1, store.Visible(showCompleted: false).Count);
        Assert.AreEqual(2, store.Visible(showCompleted: true).Count);
    }

    [TestMethod]
    public void UpdateReplacesTheTaskWithTheSameId()
    {
        var store = NewStore(out _);
        var task = Task("Write report");
        store.Add(task);

        store.Update(task with { IsComplete = true });

        Assert.IsTrue(store.Visible(true).Single().IsComplete);
    }

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

    [TestMethod]
    public void ADamagedSettingDoesNotStopTheListOpening()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(ToDoStore.SettingsKey)).Returns("[oops");

        Assert.AreEqual(0, new ToDoStore(settings.Object).Visible(true).Count);
    }
}
