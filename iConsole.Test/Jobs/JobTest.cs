using iConsole.Desktops;
using iConsole.Jobs;
using Moq;

namespace iConsole.Test.Jobs;

[TestClass]
public class JobTest
{
    private sealed class TestJob : Job
    {
        private readonly bool _throwOnRun;

        public TestJob(bool throwOnRun = false)
        {
            _throwOnRun = throwOnRun;
        }

        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool RunCalled { get; private set; }

        protected override void OnStart()
        {
            Started = true;
            base.OnStart();
        }

        protected override void OnStop()
        {
            Stopped = true;
            base.OnStop();
        }

        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            RunCalled = true;
            if (_throwOnRun)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public void OnInitialize_SetsDesktop()
    {
        TestJob job = new();
        Mock<IDesktop> desktop = new();

        job.OnInitialize(desktop.Object);

        Assert.AreSame(desktop.Object, job.Desktop);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenRunSucceeds_CallsStartAndStop()
    {
        TestJob job = new();

        await job.ExecuteAsync(CancellationToken.None);

        Assert.IsTrue(job.Started);
        Assert.IsTrue(job.RunCalled);
        Assert.IsTrue(job.Stopped);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenRunThrows_StillCallsStop()
    {
        TestJob job = new(throwOnRun: true);

        await job.ExecuteAsync(CancellationToken.None);

        Assert.IsTrue(job.Started);
        Assert.IsTrue(job.RunCalled);
        Assert.IsTrue(job.Stopped);
    }

    [TestMethod]
    public void Priority_DefaultsToBackground()
    {
        TestJob job = new();

        Assert.AreEqual(JobPriority.Background, job.Priority);
    }
}
