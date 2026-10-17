using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;
using iConsole.Helper;
using iConsole.Jobs;
using iConsole.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Buffer = iConsole.Buffers.Buffer;

namespace iConsole.Test.Jobs;

[TestClass]
public class ConsoleBufferRendererJobRunTest
{
    private static Mock<IConsoleDriver> CreateDriver(Size size)
    {
        Mock<IConsoleDriver> driver = new();
        driver.SetupGet(d => d.ForegroundColor).Returns(Color.White);
        driver.SetupGet(d => d.BackgroundColor).Returns(Color.Black);
        driver.SetupGet(d => d.WindowSize).Returns(size);
        driver.Setup(d => d.GetCursorPosition()).Returns(new Position(0, 0));
        return driver;
    }

    private static async Task RunFor(ConsoleBufferRendererJob job, int milliseconds)
    {
        using CancellationTokenSource cts = new();
        Task run = Task.Run(() => job.ExecuteAsync(cts.Token));
        await Task.Delay(milliseconds);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Run_WritesChangedLinesToTheDriver_AndFollowsWindowSize()
    {
        Mock<IConsoleDriver> driver = CreateDriver(new Size(20, 5));
        Buffer buffer = new(driver.Object);
        buffer.UpdateLayers(0);
        buffer.Write(0, new Position(0, 1), new Size(5, 1), new ConxelString("hello", Color.White, Color.Black));
        buffer.Merge();

        ConsoleBufferRendererJob job = new(driver.Object, buffer, dumpHelper: null, consoleOwnership: new ConsoleOwnership());
        Size? changed = null;
        job.WindowSizeChanged += (_, size) => changed = size;

        Task run = RunFor(job, 700);
        await Task.Delay(250);
        driver.SetupGet(d => d.WindowSize).Returns(new Size(30, 6));
        await run;

        driver.Verify(d => d.Write(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ConxelString>()), Times.AtLeastOnce());
        driver.Verify(d => d.RefreshWindowSize(), Times.AtLeastOnce());
        Assert.AreEqual(new Size(30, 6), changed);
        Assert.AreEqual(new Size(30, 6), buffer.Size);
    }

    [TestMethod]
    public async Task Run_WhileTheConsoleIsLent_WritesNothing()
    {
        Mock<IConsoleDriver> driver = CreateDriver(new Size(10, 3));
        Buffer buffer = new(driver.Object);
        buffer.UpdateLayers(0);
        buffer.Write(0, new Position(0, 0), new Size(2, 1), new ConxelString("hi", Color.White, Color.Black));
        buffer.Merge();
        ConsoleOwnership ownership = new();
        ConsoleBufferRendererJob job = new(driver.Object, buffer, null, ownership);

        using ManualResetEventSlim lentNow = new();
        using ManualResetEventSlim release = new();
        Task<int> lend = ownership.LendAsync(() =>
        {
            lentNow.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return 1;
        });
        Assert.IsTrue(lentNow.Wait(TimeSpan.FromSeconds(5)));
        driver.Invocations.Clear();

        await RunFor(job, 300);

        driver.Verify(d => d.Write(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ConxelString>()), Times.Never());
        release.Set();
        await lend;
    }

    [TestMethod]
    public async Task Run_WithDumpsEnabled_WritesFramesAndSessionTrace()
    {
        string directory = Path.Combine(Path.GetTempPath(), "render-dump-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Mock<IConsoleDriver> driver = CreateDriver(new Size(12, 3));
            Mock<IRenderingDumpHelper> dump = new();
            dump.SetupGet(d => d.IsEnabled).Returns(true);
            dump.SetupGet(d => d.DumpDirectoryPath).Returns(directory);
            Buffer buffer = new(driver.Object);
            buffer.UpdateLayers(0);
            buffer.Write(0, new Position(0, 0), new Size(4, 1), new ConxelString("dump", Color.White, Color.Black));
            buffer.Merge();
            ConsoleBufferRendererJob job = new(driver.Object, buffer, dump.Object);

            await RunFor(job, 400);

            Assert.IsTrue(File.Exists(Path.Combine(directory, "frame-000.txt")));
            dump.Verify(d => d.InitializeSession(driver.Object), Times.Once());
            dump.Verify(d => d.DumpTextArtifact("latest-rendered-buffer.txt", It.IsAny<string>()), Times.AtLeastOnce());
            Assert.AreEqual(directory, job.DumpDirectoryPath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task Run_WhenTheDumpHelperThrows_KeepsRendering()
    {
        Mock<IConsoleDriver> driver = CreateDriver(new Size(8, 2));
        Mock<IRenderingDumpHelper> dump = new();
        dump.Setup(d => d.InitializeSession(It.IsAny<IConsoleDriver>())).Throws<InvalidOperationException>();
        dump.Setup(d => d.AppendError(It.IsAny<Exception>())).Throws<InvalidOperationException>();
        Buffer buffer = new(driver.Object);
        buffer.UpdateLayers(0);
        buffer.Merge();
        ConsoleBufferRendererJob job = new(driver.Object, buffer, dump.Object);

        await RunFor(job, 200);

        dump.Verify(d => d.AppendError(It.IsAny<Exception>()), Times.AtLeastOnce());
    }

    [TestMethod]
    public void OnConsoleReacquired_ForgetsWhatWasOnScreen()
    {
        Mock<IConsoleDriver> driver = CreateDriver(new Size(6, 2));
        Buffer buffer = new(driver.Object);
        ConsoleBufferRendererJob job = new(driver.Object, buffer);
        job.LastBufferCursor.IsCursorVisible = true;

        job.OnConsoleReacquired();

        Assert.IsFalse(job.LastBufferCursor.IsCursorVisible);
    }

    [TestMethod]
    public void OnIdle_CanBeCalledRepeatedly()
    {
        Mock<IConsoleDriver> driver = CreateDriver(new Size(6, 2));
        ConsoleBufferRendererJob job = new(driver.Object, new Buffer(driver.Object));

        job.OnIdle();
        job.OnIdle();
    }
}
