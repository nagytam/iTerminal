using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;
using iConsole.Graphics;
using iConsole.Jobs;
using iConsole.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Buffer = iConsole.Buffers.Buffer;

namespace iConsole.Test.Jobs;

[TestClass]
public class ConsoleBufferRendererJobSixelTest
{
    private const string Picture = "\u001bP0;1;0q#0;2;100;0;0#0!4~\u001b\\";

    private static Mock<IConsoleDriver> CreateDriver(Size size)
    {
        Mock<IConsoleDriver> driver = new();
        driver.SetupGet(d => d.ForegroundColor).Returns(Color.White);
        driver.SetupGet(d => d.BackgroundColor).Returns(Color.Black);
        driver.SetupGet(d => d.WindowSize).Returns(size);
        driver.Setup(d => d.GetCursorPosition()).Returns(new Position(0, 0));
        return driver;
    }

    private static Buffer CreateBuffer(IConsoleDriver driver)
    {
        Buffer buffer = new(driver);
        buffer.UpdateLayers(0);
        buffer.Write(0, new Position(0, 0), new Size(20, 1), new ConxelString("a title above the picture", Color.White, Color.Black));
        // The blank cells a control paints where the picture goes.
        for (uint y = 1; y < 4; y++)
        {
            buffer.Write(0, new Position(2, y), new Size(6, 1), new ConxelString("      ", Color.White, Color.Black));
        }

        buffer.Merge();
        return buffer;
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
    public async Task Run_APictureOverBlankCells_IsDrawnOnceAtItsCorner()
    {
        Mock<IConsoleDriver> driver = CreateDriver(new Size(20, 6));
        Buffer buffer = CreateBuffer(driver.Object);
        SixelOverlay overlay = new();
        overlay.Show(this, 2, 1, 6, 3, Picture, Color.Black);
        ConsoleBufferRendererJob job = new(driver.Object, buffer, null, new ConsoleOwnership(), null, overlay);

        await RunFor(job, 400);

        driver.Verify(d => d.WriteGraphics(2, 1, Picture), Times.Once());
    }

    [TestMethod]
    public async Task Run_APictureWithTextOverItsCells_IsNotDrawn()
    {
        Mock<IConsoleDriver> driver = CreateDriver(new Size(20, 6));
        Buffer buffer = CreateBuffer(driver.Object);
        buffer.UpdateLayers(1);
        buffer.Write(1, new Position(3, 2), new Size(2, 1), new ConxelString("ok", Color.White, Color.Blue));
        buffer.Merge();
        SixelOverlay overlay = new();
        overlay.Show(this, 2, 1, 6, 3, Picture, Color.Black);
        ConsoleBufferRendererJob job = new(driver.Object, buffer, null, new ConsoleOwnership(), null, overlay);

        await RunFor(job, 400);

        driver.Verify(d => d.WriteGraphics(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
    }

    [TestMethod]
    public async Task Run_WithoutAnOverlay_NeverDrawsAPicture()
    {
        Mock<IConsoleDriver> driver = CreateDriver(new Size(20, 6));
        Buffer buffer = CreateBuffer(driver.Object);
        ConsoleBufferRendererJob job = new(driver.Object, buffer, null, new ConsoleOwnership());

        await RunFor(job, 300);

        driver.Verify(d => d.WriteGraphics(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
    }

    [TestMethod]
    public async Task Run_APictureThatIsWithdrawn_HasItsCellsRewritten()
    {
        Mock<IConsoleDriver> driver = CreateDriver(new Size(20, 6));
        Buffer buffer = CreateBuffer(driver.Object);
        SixelOverlay overlay = new();
        overlay.Show(this, 2, 1, 6, 3, Picture, Color.Black);
        ConsoleBufferRendererJob job = new(driver.Object, buffer, null, new ConsoleOwnership(), null, overlay);

        using CancellationTokenSource cts = new();
        Task run = Task.Run(() => job.ExecuteAsync(cts.Token));
        await Task.Delay(300);
        driver.Invocations.Clear();
        overlay.Hide(this);
        await Task.Delay(300);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        // Writing characters over the cells is what erases the picture from the terminal.
        driver.Verify(d => d.Write(It.IsAny<int>(), It.IsInRange(1, 3, Moq.Range.Inclusive), It.IsAny<ConxelString>()), Times.AtLeastOnce());
        driver.Verify(d => d.WriteGraphics(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
    }
}
