using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;
using iConsole.Jobs;
using Moq;
using Buffer = iConsole.Buffers.Buffer;

namespace iConsole.Test.Performance;

/// <summary>
/// Performance baseline test for the screen-update pipeline.
/// Exercises Buffer.Write + Buffer.Merge at realistic terminal size (220x50)
/// over enough iterations to produce a meaningful CPU profile.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ScreenUpdatePerformanceTest
{
    private const int Width = 220;
    private const int Height = 50;
    private const int Iterations = 500;
    private const int OverlayLayers = 3;

    private static readonly Color[] FgColors =
    [
        Color.White, Color.Yellow, Color.Cyan, Color.Green,
        Color.Red,   Color.Magenta, Color.Blue, Color.Gray
    ];

    private static readonly Color[] BgColors =
    [
        Color.Black, Color.DarkBlue, Color.DarkGreen, Color.DarkCyan,
        Color.DarkRed, Color.DarkMagenta, Color.DarkYellow, Color.DarkGray
    ];

    [TestMethod]
    [Timeout(60000)]
    public void FullFrame_WriteAndMerge_RepeatedIterations()
    {
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(Width, Height));
        mockDriver.Setup(m => m.ForegroundColor).Returns(Color.White);
        mockDriver.Setup(m => m.BackgroundColor).Returns(Color.Black);

        var buffer = new Buffer(mockDriver.Object);
        buffer.UpdateLayers(OverlayLayers);

        var random = new Random(42);

        for (int i = 0; i < Iterations; i++)
        {
            // Base layer: fill every row with a line of text
            for (int y = 0; y < Height; y++)
            {
                var fg = FgColors[y % FgColors.Length];
                var bg = BgColors[(y + i) % BgColors.Length];
                var text = new string((char)('A' + (y + i) % 26), Width);
                var conxelString = new ConxelString(text, fg, bg);
                buffer.Write(0, new Position(0, y), new Size(Width, 1), conxelString);
            }

            // Overlay layers: write shorter strings at varying positions
            for (int z = 1; z <= OverlayLayers; z++)
            {
                for (int y = 0; y < Height; y += z + 1)
                {
                    var x = (y * z + i) % (Width - 10);
                    var fg = FgColors[(y + z) % FgColors.Length];
                    var bg = BgColors[(y + z + 1) % BgColors.Length];
                    var text = new string((char)('a' + z), 10);
                    var conxelString = new ConxelString(text, fg, bg);
                    buffer.Write(z, new Position(x, y), new Size(10, 1), conxelString);
                }
            }

            buffer.Merge();
        }

        // Minimal assertion: merged layer is the expected size
        Assert.AreEqual(new Size(Width, Height), buffer.MergedLayer.Size);
    }

    [TestMethod]
    [Timeout(60000)]
    public void FullFrame_RenderDiff_RepeatedIterations()
    {
        var writeCalls = new System.Collections.Generic.List<(int x, int y)>();

        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(m => m.WindowSize).Returns(new Size(Width, Height));
        mockDriver.Setup(m => m.ForegroundColor).Returns(Color.White);
        mockDriver.Setup(m => m.BackgroundColor).Returns(Color.Black);
        mockDriver.Setup(m => m.SetCursorPosition(It.IsAny<int>(), It.IsAny<int>()));
        mockDriver.Setup(m => m.Write(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Conxel>()));
        mockDriver.Setup(m => m.Write(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ConxelString>()));
        mockDriver.SetupProperty(m => m.IsCursorVisible);
        mockDriver.SetupProperty(m => m.ForegroundColor);
        mockDriver.SetupProperty(m => m.BackgroundColor);
        mockDriver.Setup(m => m.Clear());
        mockDriver.Setup(m => m.GetCursorPosition()).Returns(new Position(0, 0));
        mockDriver.Setup(m => m.RefreshWindowSize());

        var buffer = new Buffer(mockDriver.Object);
        buffer.UpdateLayers(1);

        var dumpHelper = new iConsole.Helper.NullRenderingDumpHelper();
        var rendererJob = new ConsoleBufferRendererJob(mockDriver.Object, buffer, dumpHelper);

        var random = new Random(42);

        for (int i = 0; i < Iterations; i++)
        {
            // Alternating frame content to force dirty-region diff
            for (int y = 0; y < Height; y++)
            {
                var fg = FgColors[(y + i) % FgColors.Length];
                var bg = BgColors[y % BgColors.Length];
                var text = new string((char)('A' + (y + i) % 26), Width);
                var conxelString = new ConxelString(text, fg, bg);
                buffer.Write(0, new Position(0, y), new Size(Width, 1), conxelString);
            }

            buffer.Merge();
        }

        Assert.AreEqual(new Size(Width, Height), buffer.MergedLayer.Size);
    }
}
