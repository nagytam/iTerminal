using System.Text;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Pdb;
using iConsole.Services;
using iConsole.Test.Common;
using Moq;

namespace iConsole.Test.Pdb;

[TestClass]
public class PdbFileSummaryTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Summarize_ReadsAPortablePdbsEntryPointDocumentsAndSequencePoints()
    {
        var lines = PdbFileSummary.Summarize(SampleFiles.Bytes("example.pdb")).ToList();

        Assert.AreEqual("Debug symbols (portable PDB)", lines[0]);
        Assert.IsTrue(lines.Any(line => line.StartsWith("Metadata version: PDB v", StringComparison.Ordinal)), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(line => line.StartsWith("Id: ", StringComparison.Ordinal) && line.Contains("(stamp ", StringComparison.Ordinal)));
        Assert.IsTrue(lines.Any(line => line.StartsWith("Entry point: method token 0x06", StringComparison.Ordinal)), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(line => line.StartsWith("  ", StringComparison.Ordinal) && line.EndsWith("Program.cs", StringComparison.Ordinal)), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(line => line.StartsWith("Methods with sequence points: ", StringComparison.Ordinal) && !line.StartsWith("Methods with sequence points: 0 ", StringComparison.Ordinal)), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Summarize_ReadsAWindowsPdbsHeaderStreamsAndNamedStreams()
    {
        var lines = PdbFileSummary.Summarize(SampleFiles.Bytes("example-msf.pdb")).ToList();

        Assert.AreEqual("Windows debug symbols (PDB, MSF 7.00)", lines[0]);
        CollectionAssert.Contains(lines, "Container: 7 blocks of 4096 bytes (28,672 bytes)");
        CollectionAssert.Contains(lines, "Streams: 4, the largest is stream 1 (1 blocks)");
        CollectionAssert.Contains(lines, "Format: VC70 (20000404)");
        CollectionAssert.Contains(lines, "Timestamp: 2023-11-14 22:13:20 UTC");
        CollectionAssert.Contains(lines, "Age: 1");
        CollectionAssert.Contains(lines, "GUID: 8c1e2f40-5a6b-4c7d-9e8f-0a1b2c3d4e5f");
        CollectionAssert.Contains(lines, "Named streams: /names, /LinkInfo");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Summarize_RefusesAWindowsPdbCutShortOfItsBlocks()
    {
        byte[] msf = SampleFiles.Bytes("example-msf.pdb");

        Assert.ThrowsExactly<FormatException>(() => PdbFileSummary.Summarize(msf[..1000]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Summarize_RefusesAPortablePdbThatIsCutShort()
    {
        byte[] truncated = [.. "BSJB"u8, 1, 0, 1, 0, 0, 0, 0, 0];

        Assert.ThrowsExactly<FormatException>(() => PdbFileSummary.Summarize(truncated));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Summarize_RefusesTextAndOtherBinaryData()
    {
        Assert.ThrowsExactly<FormatException>(() => PdbFileSummary.Summarize("hello world\nnot a symbol file\n"u8.ToArray()));
        Assert.ThrowsExactly<FormatException>(() => PdbFileSummary.Summarize([0x48, 0x45, 0x41, 0x44, 0x00, 0x01]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadPdbForViewerAsync_ShowsTheWindowsSummaryAndNoBytesAsText()
    {
        var url = new Url("file://C:/x/app.pdb");
        var file = new Mock<IMemoryFile>();
        file.SetupGet(f => f.Content).Returns(SampleFiles.Bytes("example-msf.pdb"));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.ReadFileAsync(url)).ReturnsAsync(file.Object);
        var textModel = new TextModel();

        await new FileToTextConverterService().ReadPdbForViewerAsync(url, fileSystem.Object, textModel);

        Assert.AreEqual("Windows debug symbols (PDB, MSF 7.00)", textModel.Lines[0]);
        CollectionAssert.DoesNotContain(textModel.Lines.ToList(), "Source text");
        Assert.IsFalse(textModel.Lines.Any(line => line.Contains("Microsoft C/C++", StringComparison.Ordinal)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadPdbForViewerAsync_ShowsThePortableSummaryForAPortablePdb()
    {
        var url = new Url("file://C:/x/PdbSample.pdb");
        var file = new Mock<IMemoryFile>();
        file.SetupGet(f => f.Content).Returns(SampleFiles.Bytes("example.pdb"));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.ReadFileAsync(url)).ReturnsAsync(file.Object);
        var textModel = new TextModel();

        await new FileToTextConverterService().ReadPdbForViewerAsync(url, fileSystem.Object, textModel);

        Assert.AreEqual("Debug symbols (portable PDB)", textModel.Lines[0]);
        Assert.IsTrue(textModel.Lines.Any(line => line.StartsWith("Entry point: method token 0x06", StringComparison.Ordinal)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadPdbForViewerAsync_ShowsTextForAPdbThatIsNotASymbolFile()
    {
        var url = new Url("file://C:/x/notes.pdb");
        var file = new Mock<IMemoryFile>();
        file.SetupGet(f => f.Content).Returns(Encoding.UTF8.GetBytes("plain text in a file named pdb\n"));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.ReadFileAsync(url)).ReturnsAsync(file.Object);
        var textModel = new TextModel();

        await new FileToTextConverterService().ReadPdbForViewerAsync(url, fileSystem.Object, textModel);

        Assert.AreEqual("plain text in a file named pdb", textModel.Lines[0]);
    }
}
