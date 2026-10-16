using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Jobs;
using iConsole.Drivers;
using iConsole.Display;
using Buffer = iConsole.Buffers.Buffer;
using iConsole.Data;

namespace iConsole.Jobs.Test;

[TestClass]
public class ConsoleBufferRendererJobTest
{
    [TestMethod]
    public void RenderBuffer_ShouldRunWithoutThrowing()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.SetupGet(d => d.ForegroundColor).Returns(Color.White);
        mockDriver.SetupGet(d => d.BackgroundColor).Returns(Color.Black);
        mockDriver.Setup(d => d.GetCursorPosition()).Returns(new iConsole.Data.Position(0, 0));
        mockDriver.SetupGet(d => d.WindowSize).Returns(new Size(80, 24));

        var buffer = new Buffer(mockDriver.Object);
        var job = new ConsoleBufferRendererJob(mockDriver.Object, buffer);

        var method = job.GetType().GetMethod("RenderBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (bool)method!.Invoke(job, Array.Empty<object>())!;

        Assert.IsTrue(result);
    }
}

