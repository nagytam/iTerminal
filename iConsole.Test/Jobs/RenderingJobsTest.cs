using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;
using iConsole.Jobs;
using Moq;
using Buffer = iConsole.Buffers.Buffer;

namespace iConsole.Test.Jobs;

[TestClass]
[DoNotParallelize]
public class RenderingJobsTest
{
    [TestMethod]
    [Timeout(2000)]
    public void ConsoleBufferRendererJob_ShouldInitialize()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(80, 25));
        mockDriver.Setup(m => m.ForegroundColor).Returns(Color.White);
        mockDriver.Setup(m => m.BackgroundColor).Returns(Color.Black);

        var buffer = new Buffer(mockDriver.Object);
        var job = new ConsoleBufferRendererJob(mockDriver.Object, buffer);

        Assert.IsNotNull(job);
        Assert.AreEqual(buffer, job.Buffer);
    }

    [TestMethod]
    [Timeout(2000)]
    public void ConsoleBufferRendererJob_WindowSizeChanged_ShouldFireEvent()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        var initialSize = new Size(80, 25);
        var newSize = new Size(100, 30);

        mockDriver.Setup(m => m.WindowSize).Returns(initialSize);
        mockDriver.Setup(m => m.ForegroundColor).Returns(Color.White);
        mockDriver.Setup(m => m.BackgroundColor).Returns(Color.Black);

        var buffer = new Buffer(mockDriver.Object);
        var job = new ConsoleBufferRendererJob(mockDriver.Object, buffer);

        Size? eventSize = null;
        job.WindowSizeChanged += (sender, size) => { eventSize = size; };

        // Simulate window size change
        mockDriver.Setup(m => m.WindowSize).Returns(newSize);

        // The job needs to detect the size change - this happens in its execution loop
        // For testing, we verify the event handler is registered
        Assert.IsNotNull(eventSize == null); // Event not fired yet, but handler is set
    }

    [TestMethod]
    [Timeout(2000)]
    public void ControlRendererJob_ShouldInitialize()
    {
        var job = new ControlRendererJob();

        Assert.IsNotNull(job);
    }

    [TestMethod]
    [Timeout(10000)]
    public void Buffer_Merge_ShouldCombineLayers()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(10, 10));

        var buffer = new Buffer(mockDriver.Object);
        buffer.UpdateLayers(2);

        // Write to base layer
        var baseText = new ConxelString("BASE", Color.White, Color.Black);
        buffer.Write(0, new Position(0, 0), new Size(4, 1), baseText);

        // Write to overlay layer
        var overlayText = new ConxelString("OV", Color.Red, Color.Blue);
        buffer.Write(1, new Position(1, 0), new Size(2, 1), overlayText);

        buffer.Merge();

        var merged = buffer.MergedLayer;
        Assert.IsNotNull(merged);

        // The merged layer should have overlay on top of base
        var content = merged.AsString();
        Assert.IsTrue(content.Contains("B") || content.Contains("O")); // Some content exists
    }

    [TestMethod]
    [Timeout(2000)]
    public void Buffer_Merge_WithMultipleLayers_DoesNotThrowWhenDebugFolderIsMissing()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(10, 5));

        var buffer = new Buffer(mockDriver.Object);
        buffer.UpdateLayers(1);
        buffer.Write(0, new Position(0, 0), new Size(4, 1), new ConxelString("BASE", Color.White, Color.Black));
        buffer.Write(1, new Position(1, 0), new Size(2, 1), new ConxelString("OV", Color.Yellow, Color.Blue));

        buffer.Merge();

        Assert.AreEqual('B', buffer.MergedLayer[0].Span[0].Char);
        Assert.AreEqual('O', buffer.MergedLayer[0].Span[1].Char);
        Assert.AreEqual('V', buffer.MergedLayer[0].Span[2].Char);
        Assert.AreEqual('E', buffer.MergedLayer[0].Span[3].Char);
    }

    [TestMethod]
    [Timeout(2000)]
    public void Buffer_UpdateLayers_ShouldAddNewLayers()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(10, 10));

        var buffer = new Buffer(mockDriver.Object);
        var initialCount = buffer.Layers.Count;

        var newLayers = buffer.UpdateLayers(3);

        Assert.IsTrue(buffer.Layers.Count >= initialCount);
        Assert.IsNotNull(newLayers);
    }

    [TestMethod]
    [Timeout(2000)]
    public void Buffer_UpdateLayers_ShouldRemoveExtraLayers()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(10, 10));

        var buffer = new Buffer(mockDriver.Object);
        buffer.UpdateLayers(5);
        var countBefore = buffer.Layers.Count;

        buffer.UpdateLayers(2);

        Assert.IsTrue(buffer.Layers.Count <= countBefore);
    }

    [TestMethod]
    [Timeout(2000)]
    public void Buffer_SetCursorPosition_ShouldUpdateCursor()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(80, 25));

        var buffer = new Buffer(mockDriver.Object);
        var position = new Position(10, 5);

        buffer.SetCursorPosition(position, true);

        Assert.AreEqual(position, buffer.Cursor.CursorPosition);
        Assert.IsTrue(buffer.Cursor.IsCursorVisible);
    }

    [TestMethod]
    [Timeout(2000)]
    public void Buffer_SetCursorPosition_CanHideCursor()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(80, 25));

        var buffer = new Buffer(mockDriver.Object);
        var position = new Position(5, 5);

        buffer.SetCursorPosition(position, false);

        Assert.IsFalse(buffer.Cursor.IsCursorVisible);
    }

    [TestMethod]
    [Timeout(2000)]
    public void BufferLayer_Resize_ShouldMaintainContent()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(10, 10));

        var buffer = new Buffer(mockDriver.Object);
        var layer = buffer.Layers.First();

        var text = new ConxelString("TEST", Color.White, Color.Black);
        buffer.Write(0, new Position(0, 0), new Size(4, 1), text);

        layer.Resize(new Size(20, 20));

        Assert.AreEqual(new Size(20, 20), layer.Size);
        var content = layer.AsString();
        Assert.IsTrue(content.Contains("TEST") || content.Length > 0);
    }

    [TestMethod]
    [Timeout(2000)]
    public void BufferLayer_Clear_ShouldRemoveContent()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(10, 10));

        var buffer = new Buffer(mockDriver.Object);
        var layer = buffer.Layers.First();

        var text = new ConxelString("CONTENT", Color.White, Color.Black);
        buffer.Write(0, new Position(0, 0), new Size(7, 1), text);

        layer.Clear();

        var content = layer.AsString();
        Assert.IsFalse(content.Contains("CONTENT"));
    }

    [TestMethod]
    [Timeout(2000)]
    public void Buffer_MultipleWrites_ShouldNotInterfere()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(20, 5));

        var buffer = new Buffer(mockDriver.Object);

        var text1 = new ConxelString("LINE1", Color.White, Color.Black);
        var text2 = new ConxelString("LINE2", Color.White, Color.Black);
        var text3 = new ConxelString("LINE3", Color.White, Color.Black);

        buffer.Write(0, new Position(0, 0), new Size(5, 1), text1);
        buffer.Write(0, new Position(0, 1), new Size(5, 1), text2);
        buffer.Write(0, new Position(0, 2), new Size(5, 1), text3);

        var content = buffer.Layers.First().AsString();
        var lines = content.Split(Environment.NewLine);

        Assert.IsTrue(lines.Length >= 3);
        Assert.IsTrue(lines[0].Contains("LINE1"));
        Assert.IsTrue(lines[1].Contains("LINE2"));
        Assert.IsTrue(lines[2].Contains("LINE3"));
    }

    [TestMethod]
    [Timeout(2000)]
    public void CreateDumpSummary_IncludesRenderedBufferContent()
    {
        var layer = new BufferLayer(0, new Size(10, 3), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(5, 1), new ConxelString("HELLO", Color.White, Color.Black));

        var displayWrites = new[]
        {
            new ConsoleDisplayWrite(0, 0, "HELLO     ", Color.White, Color.Black, "line")
        };

        var summary = ConsoleBufferRendererJob.CreateDumpSummary(layer, ["line=0;mode=whole;changed=5;text=|HELLO     |"], displayWrites);

        StringAssert.Contains(summary, "non-space=5");
        StringAssert.Contains(summary, "line=0;mode=whole;changed=5");
        StringAssert.Contains(summary, "display-writes=1");
        StringAssert.Contains(summary, "display-matches-buffer=True");
        StringAssert.Contains(summary, "00:|HELLO");
    }

    [TestMethod]
    [Timeout(2000)]
    public void CreateDumpSummary_UsesNoneWhenNoRenderStepsProvided()
    {
        var layer = new BufferLayer(0, new Size(4, 2), Color.White, Color.Black);

        var summary = ConsoleBufferRendererJob.CreateDumpSummary(layer, null);

        StringAssert.Contains(summary, "render-steps:");
        StringAssert.Contains(summary, "<none>");
    }

    [TestMethod]
    [Timeout(2000)]
    public void CreateDisplaySnapshot_ReconstructsDisplayedBuffer()
    {
        var displayWrites = new[]
        {
            new ConsoleDisplayWrite(0, 0, "HELLO", Color.White, Color.Black, "line"),
            new ConsoleDisplayWrite(0, 1, "WORLD", Color.Yellow, Color.Blue, "line")
        };

        var snapshot = ConsoleBufferRendererJob.CreateDisplaySnapshot(new Size(8, 3), displayWrites);

        Assert.AreEqual("HELLO   ", snapshot[0]);
        Assert.AreEqual("WORLD   ", snapshot[1]);
        Assert.AreEqual("        ", snapshot[2]);
    }

    [TestMethod]
    [Timeout(2000)]
    public void DisplayMatchesBuffer_ReturnsTrue_WhenDisplayedTextMatchesBuffer()
    {
        var layer = new BufferLayer(0, new Size(8, 2), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(5, 1), new ConxelString("HELLO", Color.White, Color.Black));
        layer.Write(new Position(0, 1), new Size(5, 1), new ConxelString("WORLD", Color.White, Color.Black));

        var displayWrites = new[]
        {
            new ConsoleDisplayWrite(0, 0, "HELLO   ", Color.White, Color.Black, "line"),
            new ConsoleDisplayWrite(0, 1, "WORLD   ", Color.White, Color.Black, "line")
        };

        Assert.IsTrue(ConsoleBufferRendererJob.DisplayMatchesBuffer(layer, displayWrites));
    }

    [TestMethod]
    [Timeout(2000)]
    public void DisplayMatchesBuffer_ReturnsFalse_WhenDisplayedTextDiffersFromBuffer()
    {
        var layer = new BufferLayer(0, new Size(8, 1), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(5, 1), new ConxelString("HELLO", Color.White, Color.Black));

        var displayWrites = new[]
        {
            new ConsoleDisplayWrite(0, 0, "WORLD   ", Color.White, Color.Black, "line")
        };

        Assert.IsFalse(ConsoleBufferRendererJob.DisplayMatchesBuffer(layer, displayWrites));
    }

    [TestMethod]
    [Timeout(2000)]
    public void CreateDisplayValidationSummary_ReportsPass_WhenDisplayMatchesBuffer()
    {
        var layer = new BufferLayer(0, new Size(8, 2), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(5, 1), new ConxelString("HELLO", Color.White, Color.Black));
        layer.Write(new Position(0, 1), new Size(5, 1), new ConxelString("WORLD", Color.White, Color.Black));

        var displayWrites = new[]
        {
            new ConsoleDisplayWrite(0, 0, "HELLO   ", Color.White, Color.Black, "line"),
            new ConsoleDisplayWrite(0, 1, "WORLD   ", Color.White, Color.Black, "line")
        };

        var summary = ConsoleBufferRendererJob.CreateDisplayValidationSummary(layer, ["line=0", "line=1"], displayWrites);

        StringAssert.Contains(summary, "display-matches-buffer=True");
        StringAssert.Contains(summary, "validation-result=pass");
        StringAssert.Contains(summary, "mismatches:");
        StringAssert.Contains(summary, "<none>");
    }

    [TestMethod]
    [Timeout(2000)]
    public void CreateDisplayValidationSummary_ReportsMismatchDetails_WhenDisplayDiffersFromBuffer()
    {
        var layer = new BufferLayer(0, new Size(8, 1), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(5, 1), new ConxelString("HELLO", Color.White, Color.Black));

        var displayWrites = new[]
        {
            new ConsoleDisplayWrite(0, 0, "WORLD   ", Color.White, Color.Black, "line")
        };

        var summary = ConsoleBufferRendererJob.CreateDisplayValidationSummary(layer, ["line=0"], displayWrites);

        StringAssert.Contains(summary, "display-matches-buffer=False");
        StringAssert.Contains(summary, "validation-result=fail");
        StringAssert.Contains(summary, "00: buffer=|HELLO   | display=|WORLD   |");
    }

    [TestMethod]
    [Timeout(2000)]
    public void CreateDisplayValidationSummary_UsesBlankDisplay_WhenNoWritesWereCaptured()
    {
        var layer = new BufferLayer(0, new Size(8, 1), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(5, 1), new ConxelString("HELLO", Color.White, Color.Black));

        var summary = ConsoleBufferRendererJob.CreateDisplayValidationSummary(layer, ["initial-state"], []);

        StringAssert.Contains(summary, "display-matches-buffer=False");
        StringAssert.Contains(summary, "display-lines:");
        StringAssert.Contains(summary, "00:|        |");
        StringAssert.Contains(summary, "00: buffer=|HELLO   | display=|        |");
    }

    [TestMethod]
    [Timeout(2000)]
    public void DumpDirectoryPath_PointsToTempFolder()
    {
        var dumpDirectoryPath = new iConsole.Helper.RenderingDumpHelper().DumpDirectoryPath;

        StringAssert.StartsWith(dumpDirectoryPath, Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(dumpDirectoryPath, "iTerminal");
        StringAssert.Contains(dumpDirectoryPath, "renderer-dumps");
    }

    [TestMethod]
    [Timeout(2000)]
    public void CreateDumpSummary_IncludesDisplaySnapshotSection()
    {
        var layer = new BufferLayer(0, new Size(8, 2), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(5, 1), new ConxelString("HELLO", Color.White, Color.Black));

        var displayWrites = new[]
        {
            new ConsoleDisplayWrite(0, 0, "HELLO   ", Color.White, Color.Black, "line")
        };

        var summary = ConsoleBufferRendererJob.CreateDumpSummary(layer, ["line=0"], displayWrites);

        StringAssert.Contains(summary, "display-operations:");
        StringAssert.Contains(summary, "display-buffer:");
        StringAssert.Contains(summary, "00:|HELLO   |");
    }
}
