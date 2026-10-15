using iConsole.Controls;
using iConsole.Data;
using iConsole.Desktops;
using iConsole.Display;
using iConsole.Drivers;
using iConsole.Jobs;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Desktops;

[TestClass]
[DoNotParallelize]
public class DesktopTest
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 1000)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - startedAt).TotalMilliseconds > timeoutMilliseconds)
            {
                Assert.Fail($"Condition was not met within {timeoutMilliseconds} ms.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class TestScreen : Control, IScreen
    {
        public IDesktop Desktop { get; set; } = default!;

        public int OnIdleCallCount { get; private set; }
        public Size? LastWindowSizeChanged { get; private set; }

        public TestScreen(IThemeService themeService) : base(themeService)
        {
        }

        public override void OnIdle()
        {
            OnIdleCallCount++;
        }

        public override void OnWindowSizeChanged(object? sender, Size size)
        {
            LastWindowSizeChanged = size;
            base.OnWindowSizeChanged(sender, size);
        }
    }

    private sealed class TestLabelScreen : Control, IScreen
    {
        public IDesktop Desktop { get; set; } = default!;

        public TestLabelScreen(IThemeService themeService) : base(themeService)
        {
            AddControl(new Label("VISIBLE", themeService)
            {
                ViewSize = new Size(7, 1)
            });
        }
    }

    private sealed class PriorityCaptureJob : Job
    {
        private readonly TaskCompletionSource<ThreadPriority> _capturedPriority = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PriorityCaptureJob(JobPriority priority)
        {
            Priority = priority;
        }

        public override JobPriority Priority { get; }

        public Task<ThreadPriority> CapturedPriorityTask => _capturedPriority.Task;

        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            _capturedPriority.TrySetResult(Thread.CurrentThread.Priority);
            Desktop?.CancellationTokenSource.Cancel();
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public void GetModalChildControl_ReturnsModalChild_WhenPresent()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        var themeService = new Mock<IThemeService>();
        var buffer = new MockBuffer(new Size(10, 10));

        var screen = new TestScreen(themeService.Object);
        var modalChild = new Control(themeService.Object) { IsModal = true };
        screen.AddControl(modalChild);

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<IJob>());

        var found = desktop.GetModalChildControl();

        Assert.AreSame(modalChild, found);
    }

    [TestMethod]
    [Timeout(5000)]
    public void GetModalChildControl_ReturnsTheLastModalChild_WhenSeveralAreOpen()
    {
        // A dialog opened over a dialog is a sibling attached to the root, listed after the one
        // that opened it — that is also the order they are drawn in, so the later one is the one on
        // screen and the one the keys belong to. Reading the first instead handed every key to the
        // dialog hidden underneath: Escape closed *that* and left the visible overlay stranded.
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        var themeService = new Mock<IThemeService>();
        var buffer = new MockBuffer(new Size(10, 10));

        var screen = new TestScreen(themeService.Object);
        var underneath = new Control(themeService.Object) { IsModal = true };
        var onTop = new Control(themeService.Object) { IsModal = true };
        screen.AddControl(underneath);
        screen.AddControl(onTop);

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<IJob>());

        Assert.AreSame(onTop, desktop.GetModalChildControl());
    }

    [TestMethod]
    [Timeout(5000)]
    public void GetModalChildControl_PrefersAModalNestedInsideAModal()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        var themeService = new Mock<IThemeService>();
        var buffer = new MockBuffer(new Size(10, 10));

        var screen = new TestScreen(themeService.Object);
        var outer = new Control(themeService.Object) { IsModal = true };
        var inner = new Control(themeService.Object) { IsModal = true };
        outer.AddControl(inner);
        screen.AddControl(outer);

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<IJob>());

        Assert.AreSame(inner, desktop.GetModalChildControl());
    }

    [TestMethod]
    [Timeout(5000)]
    public void GetModalChildControl_ReturnsScreen_WhenNoModalChild()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        var themeService = new Mock<IThemeService>();
        var buffer = new MockBuffer(new Size(10, 10));

        var screen = new TestScreen(themeService.Object);

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<IJob>());

        var found = desktop.GetModalChildControl();

        Assert.AreSame(screen, found);
    }

    [TestMethod]
    [Timeout(5000)]
    public void OnIdle_SetsBufferCursorVisible_IfAnyControlWantsCursor()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        var themeService = new Mock<IThemeService>();
        var buffer = new MockBuffer(new Size(10, 10));

        var screen = new TestScreen(themeService.Object);
        var child = new Control(themeService.Object)
        {
            CanFocus = true,
            IsModal = false,
            IsCursorVisible = true
        };
        screen.AddControl(child);

        _ = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<IJob>());

        Assert.IsFalse(buffer.Cursor.IsCursorVisible);

        // act
        // Using the created desktop isn't required; the instance updates the shared buffer.
        screen.Desktop.OnIdle();

        Assert.IsTrue(buffer.Cursor.IsCursorVisible);
    }

    [TestMethod]
    [Timeout(5000)]
    public void StopAsync_ClosesScreens_CallsJobsAfterStop_AndClearsConsole()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        consoleDriver.Setup(d => d.Clear());

        var themeService = new Mock<IThemeService>();
        var buffer = new MockBuffer(new Size(10, 10));

        var screen = new Mock<IScreen>(MockBehavior.Strict);
        screen.SetupProperty(s => s.Desktop);
        screen.Setup(s => s.Close());

        var job = new Mock<IJob>(MockBehavior.Strict);
        job.Setup(j => j.OnInitialize(It.IsAny<IDesktop>()));
        job.Setup(j => j.OnAfterStop());

        var desktop = new Desktop(consoleDriver.Object, screen.Object, buffer, new[] { job.Object });

        desktop.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

        screen.Verify(s => s.Close(), Times.Once);
        job.Verify(j => j.OnAfterStop(), Times.Once);
        consoleDriver.Verify(d => d.Clear(), Times.Once);
    }

    [TestMethod]
    [Timeout(5000)]
    public void WindowSizeChangedEvent_PropagatesToFirstScreen()
    {
        var mockDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        mockDriver.SetupGet(d => d.ForegroundColor).Returns(Color.White);
        mockDriver.SetupGet(d => d.BackgroundColor).Returns(Color.Black);

        var themeService = new Mock<IThemeService>();
        var buffer = new MockBuffer(new Size(10, 10));

        var screen = new TestScreen(themeService.Object);

        var rendererJob = new ConsoleBufferRendererJob(mockDriver.Object, buffer);

        _ = new Desktop(mockDriver.Object, screen, buffer, new IJob[] { rendererJob });

        // Raise the event from the job instance. Moq.Raise doesn't work here because this is a concrete class event.
        var windowSizeChangedField = typeof(ConsoleBufferRendererJob)
            .GetField("WindowSizeChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var handler = (EventHandler<Size>?)windowSizeChangedField?.GetValue(rendererJob);
        handler?.Invoke(rendererJob, new Size(123, 45));

        Assert.AreEqual(new Size(123, 45), screen.LastWindowSizeChanged);
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task RunAsync_InitializesScreenWithConsoleWindowSize()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        var initialSize = new Size(92, 24);
        consoleDriver.Setup(d => d.RefreshWindowSize());
        consoleDriver.SetupGet(d => d.WindowSize).Returns(initialSize);
        consoleDriver.Setup(d => d.Clear());
        consoleDriver.SetupGet(d => d.ForegroundColor).Returns(Color.White);
        consoleDriver.SetupGet(d => d.BackgroundColor).Returns(Color.Black);
        consoleDriver.SetupGet(d => d.IsCursorVisible).Returns(false);

        var themeService = new Mock<IThemeService>();
        var buffer = new MockBuffer(new Size(10, 10));
        var screen = new TestScreen(themeService.Object);

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<IJob>());

        var runTask = Task.Run(desktop.RunAsync);
        await WaitUntilAsync(() => screen.LastWindowSizeChanged == initialSize && buffer.Size == initialSize, 4000);
        desktop.CancellationTokenSource.Cancel();
        await runTask;

        Assert.AreEqual(initialSize, buffer.Size);
        Assert.AreEqual(initialSize, screen.LastWindowSizeChanged);
        Assert.AreEqual(initialSize, screen.ViewSize);
        consoleDriver.Verify(d => d.RefreshWindowSize(), Times.Once);
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task RunAsync_RendersInitialScreenContentIntoBufferBeforeJobsStart()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        var initialSize = new Size(20, 5);
        consoleDriver.Setup(d => d.RefreshWindowSize());
        consoleDriver.SetupGet(d => d.WindowSize).Returns(initialSize);
        consoleDriver.Setup(d => d.Clear());

        var themeService = new Mock<IThemeService>();
        themeService.SetupGet(t => t.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 10));
        var screen = new TestLabelScreen(themeService.Object);

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<IJob>());

        var runTask = Task.Run(desktop.RunAsync);
        await WaitUntilAsync(() => buffer.ContentAsString.Contains("VISIBLE") && buffer.MergedLayer.AsString().Contains("VISIBLE"), 4000);
        desktop.CancellationTokenSource.Cancel();
        await runTask;

        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(buffer.ContentAsString, "VISIBLE");
        Assert.IsTrue(buffer.MergedLayer.AsString().Contains("VISIBLE"));
    }

    [TestMethod]
    [DataRow(JobPriority.Rendering, ThreadPriority.Highest)]
    [DataRow(JobPriority.Input, ThreadPriority.AboveNormal)]
    [DataRow(JobPriority.Background, ThreadPriority.BelowNormal)]
    [Timeout(5000)]
    public async Task RunAsync_StartsJobsOnThreadsWithMappedPriority(JobPriority jobPriority, ThreadPriority expectedThreadPriority)
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        var initialSize = new Size(20, 5);
        consoleDriver.Setup(d => d.RefreshWindowSize());
        consoleDriver.SetupGet(d => d.WindowSize).Returns(initialSize);
        consoleDriver.Setup(d => d.Clear());
        consoleDriver.SetupGet(d => d.ForegroundColor).Returns(Color.White);
        consoleDriver.SetupGet(d => d.BackgroundColor).Returns(Color.Black);
        consoleDriver.SetupGet(d => d.IsCursorVisible).Returns(false);

        var themeService = new Mock<IThemeService>();
        themeService.SetupGet(t => t.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 10));
        var screen = new TestScreen(themeService.Object);
        var job = new PriorityCaptureJob(jobPriority);

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, new IJob[] { job });

        var runTask = Task.Run(desktop.RunAsync);
        var actualThreadPriority = await job.CapturedPriorityTask;
        await runTask;

        Assert.AreEqual(expectedThreadPriority, actualThreadPriority);
    }
}
