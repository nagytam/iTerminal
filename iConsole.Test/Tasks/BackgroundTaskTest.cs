using iConsole.Tasks;

namespace iConsole.Test.Tasks;

[TestClass]
public class BackgroundTaskTest
{
    private sealed class TestBackgroundTask : BackgroundTask
    {
        private readonly bool _cancelDuringExecution;

        public TestBackgroundTask(bool cancelDuringExecution = false)
        {
            _cancelDuringExecution = cancelDuringExecution;
        }

        public void RaiseProgress(long value)
        {
            OnProgressChanged(value);
        }

        public override Task ExecuteAsync()
        {
            if (_cancelDuringExecution)
            {
                CancellationTokenSource.Cancel();
            }

            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task RunAsync_WhenExecutionCompletes_SetsCompletedStatus()
    {
        TestBackgroundTask task = new();

        await task.RunAsync();

        Assert.AreEqual(iConsole.Data.TaskStatus.Completed, task.Status);
        Assert.IsTrue(task.Stopwatch.Elapsed >= TimeSpan.Zero);
        Assert.AreEqual(task.Name, task.DisplayName);
    }

    [TestMethod]
    public async Task RunAsync_WhenCanceledDuringExecution_SetsCanceledStatus()
    {
        TestBackgroundTask task = new(cancelDuringExecution: true);

        await task.RunAsync();

        Assert.AreEqual(iConsole.Data.TaskStatus.Canceled, task.Status);
    }

    [TestMethod]
    public void OnProgressChanged_UpdatesProgressPercentage()
    {
        TestBackgroundTask task = new();

        task.RaiseProgress(25);

        Assert.AreEqual(25L, task.ProgressPercentage);
        Assert.IsTrue(task.RemainingTime >= TimeSpan.Zero);
    }
}
