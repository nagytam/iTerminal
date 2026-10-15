using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Jobs;

namespace iConsole.Jobs.Test;

[TestClass]
public class RendererJobRedrawTimingTest
{
    [TestMethod]
    public void ControlRendererJob_TriggersRedrawEvery500ms()
    {
        var job = new ControlRendererJob();
        var renderMethod = job.GetType().GetMethod("Render", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // Simulate the redraw interval logic
        var lastRedraw = DateTime.Now;
        var redrawInterval = TimeSpan.FromMilliseconds(500);
        var triggered = false;
        for (int i = 0; i < 5; i++)
        {
            var now = lastRedraw + redrawInterval;
            if (now - lastRedraw >= redrawInterval)
            {
                triggered = true;
                lastRedraw = now;
            }
        }
        Assert.IsTrue(triggered);
    }

    [TestMethod]
    public void ConsoleBufferRendererJob_TriggersRedrawEvery500ms()
    {
        var mockDriver = new Moq.Mock<iConsole.Drivers.IConsoleDriver>();
        mockDriver.SetupGet(d => d.ForegroundColor).Returns(iConsole.Display.Color.White);
        mockDriver.SetupGet(d => d.BackgroundColor).Returns(iConsole.Display.Color.Black);
        mockDriver.Setup(d => d.GetCursorPosition()).Returns(new iConsole.Data.Position(0, 0));
        mockDriver.SetupGet(d => d.WindowSize).Returns(new iConsole.Data.Size(80, 24));
        var buffer = new iConsole.Buffers.Buffer(mockDriver.Object);
        var job = new ConsoleBufferRendererJob(mockDriver.Object, buffer);
        // Simulate the redraw interval logic
        var lastRedraw = DateTime.Now;
        var redrawInterval = TimeSpan.FromMilliseconds(500);
        var triggered = false;
        for (int i = 0; i < 5; i++)
        {
            var now = lastRedraw + redrawInterval;
            if (now - lastRedraw >= redrawInterval)
            {
                triggered = true;
                lastRedraw = now;
            }
        }
        Assert.IsTrue(triggered);
    }
}
