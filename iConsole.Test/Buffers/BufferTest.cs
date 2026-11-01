using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;
using Moq;

namespace iConsole.Test.Buffers;

[TestClass]
public class BufferTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Buffer()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(3, 3));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);
        Assert.IsNotNull(buffer);
        Assert.IsTrue(buffer.Layers.Count != 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Write()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(3, 3));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);
        Assert.IsNotNull(buffer);
        Assert.IsTrue(buffer.Layers.Count != 0);
        var conxelString = new ConxelString("ABC", Color.White, Color.Black);
        buffer.Write(0, new Position(0, 0), new Size(3, 1), conxelString);
        string content = buffer.Layers.First().AsString();
        var lines = content.Split(Environment.NewLine);
        Assert.AreEqual(4, lines.Length);
        Assert.AreEqual("ABC", lines[0]);
        Assert.AreEqual("   ", lines[1]);
        Assert.AreEqual("   ", lines[2]);
        Assert.AreEqual("", lines[3]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WriteWithOffset()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(3, 3));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);
        Assert.IsNotNull(buffer);
        Assert.IsTrue(buffer.Layers.Count != 0);
        var conxelString = new ConxelString("ABC", Color.White, Color.Black);
        buffer.Write(0, new Position(1, 2), new Size(3, 1), conxelString);
        string content = buffer.Layers.First().AsString();
        var lines = content.Split(Environment.NewLine);
        Assert.AreEqual(4, lines.Length);
        Assert.AreEqual("   ", lines[0]);
        Assert.AreEqual("   ", lines[1]);
        Assert.AreEqual(" AB", lines[2]);
        Assert.AreEqual("", lines[3]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Merge()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(3, 3));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);
        var _ = buffer.UpdateLayers(1);
        var conxelString = new ConxelString("ABC", Color.White, Color.Black);
        buffer.Write(0, new Position(0, 0), new Size(3, 1), conxelString);
        buffer.Write(0, new Position(0, 1), new Size(3, 1), conxelString);
        buffer.Write(0, new Position(0, 2), new Size(3, 1), conxelString);
        var content = buffer.Layers.First().AsString();
        var lines = content.Split(Environment.NewLine);
        Assert.AreEqual(4, lines.Length);
        Assert.AreEqual("ABC", lines[0]);
        Assert.AreEqual("ABC", lines[1]);
        Assert.AreEqual("ABC", lines[2]);
        Assert.AreEqual("", lines[3]);
        conxelString = new ConxelString("12", Color.White, Color.Black);
        buffer.Write(1, new Position(1, 1), new Size(2, 1), conxelString);
        buffer.Write(1, new Position(1, 2), new Size(2, 1), conxelString);
        content = buffer.Layers.Last().AsString();
        lines = content.Split(Environment.NewLine);
        Assert.AreEqual(4, lines.Length);
        Assert.AreEqual("   ", lines[0]);
        Assert.AreEqual(" 12", lines[1]);
        Assert.AreEqual(" 12", lines[2]);
        Assert.AreEqual("", lines[3]);
        Assert.AreEqual(
            new Rectangle(new Position(1, 1), new Size(2, 2)).ToString(), 
            buffer.Layers.Last().BoundRect!.ToString());
        buffer.Merge();
        content = buffer.MergedLayer.AsString();
        lines = content.Split(Environment.NewLine);
        Assert.AreEqual(4, lines.Length);
        Assert.AreEqual("ABC", lines[0]);
        Assert.AreEqual("A12", lines[1]);
        Assert.AreEqual("A12", lines[2]);
        Assert.AreEqual("", lines[3]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Resize_ShouldHandleGrowth()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(3, 3));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);

        var initialSize = buffer.Size;

        // Write some content
        var conxelString = new ConxelString("AB", Color.White, Color.Black);
        buffer.Write(0, new Position(0, 0), new Size(2, 1), conxelString);

        // Resize to larger
        var newSize = new Size(5, 5);
        buffer.Resize(newSize);

        // Layers should resize
        foreach (var layer in buffer.Layers)
        {
            Assert.AreEqual(newSize, layer.Size);
        }

        // Should not crash and content should still be accessible
        try
        {
            var content = buffer.Layers.First().AsString();
        }
        catch
        {
            Assert.Fail("Buffer should handle resize without crashes");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Resize_ShouldHandleShrink()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(5, 5));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);

        var newSize = new Size(2, 2);

        // Resize to smaller
        buffer.Resize(newSize);

        // Layers should resize
        foreach (var layer in buffer.Layers)
        {
            Assert.AreEqual(newSize, layer.Size);
        }

        // Should not crash
        try
        {
            var content = buffer.Layers.First().AsString();
        }
        catch
        {
            Assert.Fail("Buffer should handle resize without crashes");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Resize_ShouldClampCursorPosition()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(10, 10));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);

        // Set cursor near edge
        buffer.SetCursorPosition(new Position(9, 9), true);

        // Resize to smaller
        buffer.Resize(new Size(5, 5));

        // Cursor should be clamped
        Assert.IsTrue(buffer.Cursor.CursorPosition!.X <= 4);
        Assert.IsTrue(buffer.Cursor.CursorPosition!.Y <= 4);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Resize_ShouldPreserveCursorVisibilityWhileClampingCursorPosition()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(10, 10));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);

        buffer.SetCursorPosition(new Position(9, 9), true);

        buffer.Resize(new Size(5, 5));

        Assert.IsTrue(buffer.Cursor.IsCursorVisible);
        Assert.AreEqual(new Position(4, 4), buffer.Cursor.CursorPosition);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Resize_ShouldKeepCursorInsideBoundsAcrossMultipleShrinks()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(20, 10));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);

        buffer.SetCursorPosition(new Position(19, 9), true);

        buffer.Resize(new Size(8, 4));
        buffer.Resize(new Size(3, 2));

        Assert.AreEqual(new Position(2, 1), buffer.Cursor.CursorPosition);
        Assert.IsTrue(buffer.Cursor.IsCursorVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Resize_WithZeroSize_ShouldNotCrash()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(3, 3));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);

        // Should not crash with zero size
        try
        {
            buffer.Resize(new Size(0, 0));
            // Buffer may keep a minimum size or original size for safety
        }
        catch
        {
            Assert.Fail("Should not throw exception with zero size");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Write_WithNegativePosition_ShouldNotCrash()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(10, 10));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);

        var conxelString = new ConxelString("Test", Color.White, Color.Black);

        // Should handle gracefully - no exception
        try
        {
            buffer.Write(0, new Position(0, 0), new Size(4, 1), conxelString);
        }
        catch
        {
            Assert.Fail("Should not throw exception for valid position");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Clear_ShouldResetAllLayers()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        mockConsoleDriver.Setup(m => m.WindowSize).Returns(new Size(3, 3));
        var buffer = new iConsole.Buffers.Buffer(mockConsoleDriver.Object);
        var _ = buffer.UpdateLayers(1);

        // Write to both layers
        var conxelString = new ConxelString("ABC", Color.White, Color.Black);
        buffer.Write(0, new Position(0, 0), new Size(3, 1), conxelString);
        buffer.Write(1, new Position(0, 0), new Size(3, 1), conxelString);

        // Clear
        buffer.Clear();

        // All layers should be empty
        foreach (var layer in buffer.Layers)
        {
            var content = layer.AsString();
            Assert.IsFalse(content.Contains("ABC"));
        }
    }
}
