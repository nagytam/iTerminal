using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Test.Helper;

[TestClass]
public class RenderingDumpHelperCoverageTest
{
    [TestMethod]
    public void NullRenderingDumpHelper_IsDisabledAndNoOp()
    {
        NullRenderingDumpHelper helper = NullRenderingDumpHelper.Instance;
        BufferLayer layer = new(0, new Size(4, 1), Color.White, Color.Black);

        helper.InitializeSession(NullConsoleDriver.Instance);
        helper.AppendSessionTrace("trace");
        helper.DumpBufferStage("stage", layer);
        helper.DumpTextArtifact("artifact.txt", "content");
        helper.AppendError(new InvalidOperationException("boom"));

        Assert.IsFalse(helper.IsEnabled);
        Assert.AreEqual(string.Empty, helper.DumpDirectoryPath);
    }

    [TestMethod]
    public void RenderingDumpHelper_CreateWindowSummary_IncludesDriverAndWindowSize()
    {
        string summary = RenderingDumpHelper.CreateWindowSummary(NullConsoleDriver.Instance);

        StringAssert.Contains(summary, typeof(NullConsoleDriver).FullName);
        StringAssert.Contains(summary, "window-size=0x0");
    }

    [TestMethod]
    public void RenderingDumpHelper_CreateBufferSummary_ReportsStageAndVisibleCharacters()
    {
        BufferLayer layer = new(0, new Size(4, 1), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(4, 1), new ConxelString("A  ", Color.White, Color.Black));

        string summary = RenderingDumpHelper.CreateBufferSummary("paint", layer, ["detail"]);

        StringAssert.Contains(summary, "stage=paint");
        StringAssert.Contains(summary, "non-space=1");
        StringAssert.Contains(summary, "detail");
    }

    [TestMethod]
    public void RenderingDumpHelper_SanitizeFileName_ReplacesInvalidCharacters()
    {
        char invalidCharacter = Path.GetInvalidFileNameChars().First(character => character != '\0');

        string sanitized = RenderingDumpHelper.SanitizeFileName($"before{invalidCharacter}after");

        Assert.AreEqual("before-after", sanitized);
    }
}
