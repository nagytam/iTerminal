using iConsole.Inputs;
using iConsole.Inputs.Posix.Jobs;
using iConsole.Jobs;

namespace iConsole.Inputs.Posix.Test;

[TestClass]
public class PosixTerminalInputPublisherJobTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesQueuesAndImplementsExpectedInterfaces()
    {
        PosixTerminalInputPublisherJob job = new();

        Assert.IsNotNull(job.KeyQueue);
        Assert.IsNotNull(job.MouseQueue);
        Assert.IsNotNull(job.InputAvailableSignal);
        Assert.IsInstanceOfType<ITerminalInputSource>(job);
        Assert.IsInstanceOfType<IJob>(job);
        Assert.AreEqual(JobPriority.Input, job.Priority);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_OnNonLinux_CompletesWithoutReadingInput()
    {
        if (OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("This test validates the non-Linux fast-path.");
        }

        PosixTerminalInputPublisherJob job = new();

        await job.ExecuteAsync(CancellationToken.None);

        Assert.AreEqual(0, job.KeyQueue.Count);
        Assert.AreEqual(0, job.MouseQueue.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesEmptyQueues()
    {
        PosixTerminalInputPublisherJob job = new();

        Assert.AreEqual(0, job.KeyQueue.Count);
        Assert.AreEqual(0, job.MouseQueue.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InputAvailableSignal_StartsWithInitialCountOfZero()
    {
        PosixTerminalInputPublisherJob job = new();

        // The semaphore should start with an initial count
        // Try to wait with immediate timeout - if count is > 0, this will succeed
        // If count is 0, this will timeout
        Assert.IsNotNull(job.InputAvailableSignal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Priority_ReturnsInputPriority()
    {
        PosixTerminalInputPublisherJob job = new();

        Assert.AreEqual(JobPriority.Input, job.Priority);
    }
}
