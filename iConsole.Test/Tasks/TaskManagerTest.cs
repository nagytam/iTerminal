using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace iConsole.Tasks.Test;

[TestClass]
public class TaskManagerTest
{
    private sealed class FakeTask : IBackgroundTask
    {
        private readonly Func<Task>? _runAsync;

        public string Name => "Fake";
        public string DisplayName => "Fake Task";
        public TaskPriority Priority { get; init; } = TaskPriority.Background;
        public CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

        public FakeTask(Func<Task>? runAsync = null)
        {
            _runAsync = runAsync;
        }

        public Task RunAsync() => _runAsync?.Invoke() ?? Task.CompletedTask;
    }

    /// <summary>
    /// The container used to pick the constructor with the defaulted worker count, which ran the app
    /// on one worker: a folder copy's discovery task then starved the copy that drains it.
    /// </summary>
    [TestMethod]
    public void ResolvedFromTheContainer_RunsSeveralTasksAtOnce()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<TaskManager>(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

        var manager = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<TaskManager>(provider);

        Assert.IsTrue(manager.WorkerCount >= 2, $"{manager.WorkerCount} worker(s)");
        manager.Dispose();
    }

    [TestMethod]
    public async Task Add_ShouldInsertAndCancelByType()
    {
        // A single worker held busy by a gated blocker keeps the task queued: an idle worker would
        // run and remove it before the assertions below, and CancelTask would find nothing to cancel.
        using var manager = new TaskManager(workerCount: 1);
        var release = new SemaphoreSlim(0, 1);
        var task = new FakeTask();

        await manager.Add(new FakeTask(() => release.WaitAsync()));
        await manager.Add(task);
        Assert.IsTrue(manager.Tasks.Any(t => t == task));

        manager.CancelTask(typeof(FakeTask));
        Assert.IsTrue(task.CancellationTokenSource.IsCancellationRequested);

        release.Release();
    }

    [TestMethod]
    public async Task Add_ShouldRunTasksSequentiallyWhenWorkerCountIsOne()
    {
        using var manager = new TaskManager(workerCount: 1);
        var started = new SemaphoreSlim(0, 1);
        var release = new SemaphoreSlim(0, 1);
        var executionOrder = new ConcurrentQueue<string>();

        var firstTask = new FakeTask(async () =>
        {
            executionOrder.Enqueue("first-start");
            started.Release();
            await release.WaitAsync();
            executionOrder.Enqueue("first-end");
        });

        var secondTask = new FakeTask(() =>
        {
            executionOrder.Enqueue("second-run");
            return Task.CompletedTask;
        });

        await manager.Add(firstTask);
        await started.WaitAsync();
        await manager.Add(secondTask);

        await Task.Delay(50);
        CollectionAssert.AreEqual(new[] { "first-start" }, executionOrder.ToArray());

        release.Release();

        await AssertEventuallyAsync(() => executionOrder.Count == 3);
        CollectionAssert.AreEqual(new[] { "first-start", "first-end", "second-run" }, executionOrder.ToArray());
    }

    [TestMethod]
    public async Task Add_ShouldRunHigherPriorityTaskBeforeLowerPriorityTask()
    {
        using var manager = new TaskManager(workerCount: 1);
        var executionOrder = new ConcurrentQueue<string>();

        // A gate holds the only worker while both tasks are queued. Without it the worker can take the
        // background task before the input task arrives, and the order then depends on scheduling.
        var gate = new SemaphoreSlim(0, 1);
        var gateTask = new FakeTask(async () => await gate.WaitAsync())
        {
            Priority = TaskPriority.Rendering,
        };

        var backgroundTask = new FakeTask(() =>
        {
            executionOrder.Enqueue("background");
            return Task.CompletedTask;
        })
        {
            Priority = TaskPriority.Background,
        };

        var inputTask = new FakeTask(() =>
        {
            executionOrder.Enqueue("input");
            return Task.CompletedTask;
        })
        {
            Priority = TaskPriority.Input,
        };

        await manager.Add(gateTask);
        await manager.Add(backgroundTask);
        await manager.Add(inputTask);
        gate.Release();

        await AssertEventuallyAsync(() => executionOrder.Count == 2);
        CollectionAssert.AreEqual(new[] { "input", "background" }, executionOrder.ToArray());
    }

    [TestMethod]
    public async Task Add_ShouldSkipCanceledQueuedTask()
    {
        using var manager = new TaskManager(workerCount: 1);
        var started = new SemaphoreSlim(0, 1);
        var release = new SemaphoreSlim(0, 1);
        var skippedTaskExecuted = false;

        var blockingTask = new FakeTask(async () =>
        {
            started.Release();
            await release.WaitAsync();
        });
        var canceledTask = new FakeTask(() =>
        {
            skippedTaskExecuted = true;
            return Task.CompletedTask;
        });

        await manager.Add(blockingTask);
        await started.WaitAsync();
        await manager.Add(canceledTask);

        manager.CancelTask(typeof(FakeTask));
        release.Release();

        await AssertEventuallyAsync(() => !manager.Tasks.Any());
        Assert.IsFalse(skippedTaskExecuted);
    }

    private static async Task AssertEventuallyAsync(Func<bool> condition, int timeoutMs = 1000)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - startedAt > TimeSpan.FromMilliseconds(timeoutMs))
            {
                Assert.Fail("Condition was not met within the expected timeout.");
            }

            await Task.Delay(10);
        }
    }
}
