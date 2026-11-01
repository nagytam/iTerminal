using iConsole.Data;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Services.Taskbar;

namespace iTerminal.Core.Test.Services.Taskbar;

[TestClass]
public class TaskbarProgressTest
{
    /// <summary>A task whose progress and status the test sets directly.</summary>
    private sealed class FakeTask : BackgroundTask
    {
        public FakeTask(long progress = 0) => ProgressPercentage = progress;

        public override Task ExecuteAsync() => Task.CompletedTask;

        public void Report(long percentage) => ProgressPercentage = percentage;

        public void Finish() => Status = iConsole.Data.TaskStatus.Completed;

        public void Pause() => PauseGate.Pause();

        public void Resume() => PauseGate.Resume();
    }

    /// <summary>An indicator that records what it was told.</summary>
    private sealed class RecordingTaskbar : ITaskbarProgress
    {
        public List<(TaskbarState State, long Percentage)> Calls { get; } = [];

        public void Update(TaskbarState state, long percentage) => Calls.Add((state, percentage));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingRunning_ShowsNoIndicator()
    {
        var result = TaskbarProgressCalculator.Compute([]);

        Assert.AreEqual((TaskbarState.None, 0L), result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RunningTasks_ShowTheirAverageProgress()
    {
        var tasks = new IBackgroundTask[] { new FakeTask(20), new FakeTask(60) };

        Assert.AreEqual((TaskbarState.Normal, 40L), TaskbarProgressCalculator.Compute(tasks));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RunningWithNoProgressYet_ShowsAMovingIndicator()
    {
        var tasks = new IBackgroundTask[] { new FakeTask(0) };

        Assert.AreEqual((TaskbarState.Indeterminate, 0L), TaskbarProgressCalculator.Compute(tasks));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryRunningTaskPaused_ShowsPaused()
    {
        var task = new FakeTask(50);
        task.Pause();

        Assert.AreEqual((TaskbarState.Paused, 50L), TaskbarProgressCalculator.Compute([task]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OneRunningTaskAmongPaused_ShowsNormal()
    {
        var paused = new FakeTask(10);
        paused.Pause();
        var running = new FakeTask(30);

        Assert.AreEqual((TaskbarState.Normal, 20L), TaskbarProgressCalculator.Compute([paused, running]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FinishedTasks_DoNotCount()
    {
        var finished = new FakeTask(100);
        finished.Finish();

        Assert.AreEqual((TaskbarState.None, 0L), TaskbarProgressCalculator.Compute([finished]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Job_TellsTheIndicatorOnlyWhenWhatItShowsChanges()
    {
        var manager = new TaskManager(1);
        var taskbar = new RecordingTaskbar();
        var job = new TaskbarProgressJob(manager, taskbar);
        var task = new FakeTask(0);
        manager.Tasks.Add(task);

        job.Publish();
        job.Publish();
        task.Report(40);
        job.Publish();
        job.Publish();

        // The task was running (at 0%) before the first poll, so the first call is indeterminate; two polls at 40% make one call.
        CollectionAssert.AreEqual(
            new[] { (TaskbarState.Indeterminate, 0L), (TaskbarState.Normal, 40L) },
            taskbar.Calls);
        manager.Dispose();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Job_ClearsTheIndicatorWhenTheWorkIsDone()
    {
        var manager = new TaskManager(1);
        var taskbar = new RecordingTaskbar();
        var job = new TaskbarProgressJob(manager, taskbar);
        var task = new FakeTask(70);
        manager.Tasks.Add(task);
        job.Publish();

        task.Finish();
        job.Publish();

        Assert.AreEqual((TaskbarState.None, 0L), taskbar.Calls[^1]);
        manager.Dispose();
    }
}
