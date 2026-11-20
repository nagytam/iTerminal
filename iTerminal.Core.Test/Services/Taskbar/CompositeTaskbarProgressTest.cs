using iTerminal.Core.Services.Taskbar;

namespace iTerminal.Core.Test.Services.Taskbar;

[TestClass]
public class CompositeTaskbarProgressTest
{
    private sealed class RecordingTaskbar : ITaskbarProgress
    {
        public List<(TaskbarState State, long Percentage)> Updates { get; } = [];

        public void Update(TaskbarState state, long percentage) => Updates.Add((state, percentage));
    }

    private sealed class ThrowingTaskbar : ITaskbarProgress
    {
        public void Update(TaskbarState state, long percentage) => throw new InvalidOperationException("no shell taskbar");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Update_ForwardsToEveryInner()
    {
        var first = new RecordingTaskbar();
        var second = new RecordingTaskbar();
        var composite = new CompositeTaskbarProgress(first, second);

        composite.Update(TaskbarState.Normal, 25);

        CollectionAssert.AreEqual(new[] { (TaskbarState.Normal, 25L) }, first.Updates);
        CollectionAssert.AreEqual(new[] { (TaskbarState.Normal, 25L) }, second.Updates);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Update_AFailingFirstInner_DoesNotStopTheSecond()
    {
        var second = new RecordingTaskbar();
        var composite = new CompositeTaskbarProgress(new ThrowingTaskbar(), second);

        composite.Update(TaskbarState.Paused, 80);

        CollectionAssert.AreEqual(new[] { (TaskbarState.Paused, 80L) }, second.Updates);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Dispose_DisposesTheDisposableInnerImplementations()
    {
        var disposable = new DisposableTaskbar();
        var composite = new CompositeTaskbarProgress(new RecordingTaskbar(), disposable);

        composite.Dispose();

        Assert.IsTrue(disposable.IsDisposed);
    }

    private sealed class DisposableTaskbar : ITaskbarProgress, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Update(TaskbarState state, long percentage)
        {
        }

        public void Dispose() => IsDisposed = true;
    }
}
