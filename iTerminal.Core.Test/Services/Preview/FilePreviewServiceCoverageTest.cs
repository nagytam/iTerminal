using System.Text;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Preview;
using Moq;

namespace iTerminal.Core.Test.Services.Preview;

[TestClass]
public class FilePreviewServiceCoverageTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "preview_cov_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteTree()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private static Task<FilePreview> BuildAsync(IEntry? entry, IFileSystem? fileSystem = null, int width = 60, IFileToTextConverterService? converter = null, CancellationToken token = default) =>
        new FilePreviewService(converter).BuildAsync(entry, fileSystem, width, token);

    [Timeout(60_000)]
    [TestMethod]
    public async Task NoEntry_AndTheWayUpRow_HaveNothingToShow()
    {
        FilePreview none = await BuildAsync(null);
        StringAssert.Contains(string.Join(" ", none.Lines), "Nothing under the cursor");

        Mock<IEntry> up = new();
        up.SetupGet(e => e.DisplayName).Returns("..");
        up.SetupGet(e => e.Url).Returns(new Url(_root));
        FilePreview way = await BuildAsync(up.Object);
        StringAssert.Contains(string.Join(" ", way.Lines), "The way up");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AVanishedFileOrFolder_SaysItIsGone()
    {
        FilePreview file = await BuildAsync(new FileSystemEntry(Path.Combine(_root, "gone.txt")));
        StringAssert.Contains(string.Join(" ", file.Lines), "The file is gone");

        Mock<IEntry> folder = new();
        folder.SetupGet(e => e.DisplayName).Returns("vanished");
        folder.SetupGet(e => e.Url).Returns(new Url(Path.Combine(_root, "vanished") + Path.DirectorySeparatorChar));
        FilePreview preview = await BuildAsync(folder.Object);
        StringAssert.Contains(string.Join(" ", preview.Lines), "gone");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderListsFoldersFirst_AndCutsAfterFiveHundredRows()
    {
        string big = Path.Combine(_root, "big");
        Directory.CreateDirectory(Path.Combine(big, "sub"));
        for (int i = 0; i < 510; i++)
        {
            File.WriteAllText(Path.Combine(big, $"f{i:D3}.txt"), "x");
        }

        FilePreview preview = await BuildAsync(new FileSystemEntry(big));

        StringAssert.Contains(preview.Summary, "1 folder");
        Assert.IsTrue(preview.Lines.Any(l => l.Contains("sub")));
        Assert.IsTrue(preview.Lines.Any(l => l.Contains("… and 11 more")), string.Join("\n", preview.Lines.TakeLast(3)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyFolder_ShowsZeroCounts()
    {
        string empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        FilePreview preview = await BuildAsync(new FileSystemEntry(empty));

        Assert.AreEqual("0 folders, 0 files", preview.Summary);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARemoteFolder_IsListedThroughItsDrive_OrExplained()
    {
        Url folder = new("docker://web/etc/");
        Mock<IFolderListingFileSystem> listing = new();
        listing.Setup(l => l.ListChildren(It.IsAny<Url>())).Returns([new Url("docker://web/etc/conf.d/"), new Url("docker://web/etc/motd")]);
        VirtualDriveEntry entry = new(folder, "etc");

        FilePreview listed = await BuildAsync(entry, listing.Object);
        FilePreview plain = await BuildAsync(entry, Mock.Of<IFileSystem>());

        Assert.AreEqual("2 items", listed.Summary);
        Assert.IsTrue(listed.Lines.Any(l => l.Contains("conf.d/")));
        StringAssert.Contains(string.Join(" ", plain.Lines), "A folder");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADriveThatKnowsBetter_AnswersFirst()
    {
        Mock<IFileSystem> drive = new();
        Mock<IEntryPreviewSource> source = drive.As<IEntryPreviewSource>();
        FilePreview own = new("own", "summary", ["line"]);
        VirtualDriveEntry entry = new(new Url("docker://web/c1"), "c1");
        source.Setup(s => s.BuildPreviewAsync(entry, 60, It.IsAny<CancellationToken>())).ReturnsAsync(own);

        Assert.AreSame(own, await BuildAsync(entry, drive.Object));

        source.Setup(s => s.BuildPreviewAsync(entry, 60, It.IsAny<CancellationToken>())).ReturnsAsync((FilePreview?)null);
        FilePreview fallback = await BuildAsync(entry, drive.Object);
        Assert.AreEqual("c1", fallback.Title, "with no own answer the ordinary preview is built");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADocument_IsRenderedThroughTheConverter_AndWrapped()
    {
        string path = Path.Combine(_root, "doc.docx");
        File.WriteAllText(path, "placeholder");
        Mock<IFileToTextConverterService> converter = new();
        converter.Setup(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()))
            .Callback<Url?, IFileSystem, ITextModel>((_, _, model) =>
            {
                model.Lines.Add("short");
                model.Lines.Add(string.Join(" ", Enumerable.Repeat("wrapped", 20)));
            })
            .Returns(Task.CompletedTask);

        FilePreview preview = await BuildAsync(new FileSystemEntry(path), Mock.Of<IFileSystem>(), 30, converter.Object);

        StringAssert.Contains(preview.Summary, "DOCX");
        Assert.IsTrue(preview.Lines.All(l => l.Length <= 30));
        Assert.IsGreaterThan(2, preview.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADocumentThatRendersNothing_FallsBackToTheRawHead()
    {
        string path = Path.Combine(_root, "blank.docx");
        File.WriteAllText(path, "raw text");
        Mock<IFileToTextConverterService> converter = new();
        converter.Setup(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()))
            .Callback<Url?, IFileSystem, ITextModel>((_, _, model) => model.Lines.Add("   "))
            .Returns(Task.CompletedTask);

        FilePreview preview = await BuildAsync(new FileSystemEntry(path), Mock.Of<IFileSystem>(), 40, converter.Object);

        Assert.IsTrue(preview.Lines.Contains("raw text"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARenderedDocumentIsCappedAtMaxLines()
    {
        string path = Path.Combine(_root, "long.docx");
        File.WriteAllText(path, "x");
        Mock<IFileToTextConverterService> converter = new();
        converter.Setup(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()))
            .Callback<Url?, IFileSystem, ITextModel>((_, _, model) =>
            {
                for (int i = 0; i < FilePreviewService.MaxLines + 50; i++)
                {
                    model.Lines.Add("line " + i);
                }
            })
            .Returns(Task.CompletedTask);

        FilePreview preview = await BuildAsync(new FileSystemEntry(path), Mock.Of<IFileSystem>(), 40, converter.Object);

        Assert.AreEqual(FilePreviewService.MaxLines, preview.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Cancellation_Propagates()
    {
        string path = Path.Combine(_root, "a.txt");
        File.WriteAllText(path, "text");
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => BuildAsync(new FileSystemEntry(path), token: cts.Token));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARemoteDriveThatCannotStream_IsReadWhole()
    {
        Mock<IFileSystem> drive = new();
        drive.Setup(d => d.StreamToAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotImplementedException());
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(Encoding.UTF8.GetBytes("whole file"));
        drive.Setup(d => d.ReadFileAsync(It.IsAny<Url>())).ReturnsAsync(file.Object);
        VirtualDriveEntry entry = new(new Url("ssh://host/etc/motd"), "motd") { Size = 10 };

        FilePreview preview = await BuildAsync(entry, drive.Object);

        Assert.IsTrue(preview.Lines.Contains("whole file"));

        Mock<IMemoryFile> huge = new();
        huge.SetupGet(f => f.Content).Returns(new byte[FilePreviewService.MaxTextBytes + 100]);
        drive.Setup(d => d.ReadFileAsync(It.IsAny<Url>())).ReturnsAsync(huge.Object);
        Assert.IsNotNull(await BuildAsync(entry, drive.Object));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARemoteFileWithoutADrive_CannotBeRead()
    {
        VirtualDriveEntry entry = new(new Url("ssh://host/etc/motd"), "motd");

        FilePreview preview = await BuildAsync(entry, null);

        StringAssert.Contains(string.Join(" ", preview.Lines), "This drive cannot be read");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Utf16AndBomEncodings_AreDecoded()
    {
        string le = Path.Combine(_root, "le.txt");
        File.WriteAllBytes(le, [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("little")]);
        string be = Path.Combine(_root, "be.txt");
        File.WriteAllBytes(be, [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes("big")]);
        string bom = Path.Combine(_root, "bom.txt");
        File.WriteAllBytes(bom, [0xEF, 0xBB, 0xBF, .. "utf8"u8.ToArray()]);
        string empty = Path.Combine(_root, "none.txt");
        File.WriteAllBytes(empty, []);

        FilePreview littleEndian = await BuildAsync(new FileSystemEntry(le));
        FilePreview bigEndian = await BuildAsync(new FileSystemEntry(be));
        FilePreview withBom = await BuildAsync(new FileSystemEntry(bom));
        FilePreview nothing = await BuildAsync(new FileSystemEntry(empty));

        StringAssert.Contains(littleEndian.Summary, "UTF-16");
        Assert.IsTrue(littleEndian.Lines.Contains("little"));
        StringAssert.Contains(bigEndian.Summary, "UTF-16 BE");
        Assert.IsTrue(bigEndian.Lines.Contains("big"));
        Assert.IsTrue(withBom.Lines.Contains("utf8"));
        Assert.IsNotNull(nothing);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATextHeadCutInsideACharacter_IsStillUtf8()
    {
        string path = Path.Combine(_root, "cut.txt");
        byte[] bytes = [.. Enumerable.Repeat((byte)'a', FilePreviewService.MaxTextBytes - 1), .. "é"u8.ToArray(), .. "tail"u8.ToArray()];
        File.WriteAllBytes(path, bytes);

        FilePreview preview = await BuildAsync(new FileSystemEntry(path));

        StringAssert.Contains(preview.Summary, "UTF-8");
        StringAssert.Contains(preview.Summary, "first part");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LooksBinary_IsTrueForNulsAndControlSoup_AndFalseForTextAndUtf16()
    {
        Assert.IsTrue(FilePreviewService.LooksBinary([1, 0, 2]));
        Assert.IsTrue(FilePreviewService.LooksBinary([1, 2, 3, 4, 5, 6, 7, 8, 14, 15]));
        Assert.IsFalse(FilePreviewService.LooksBinary("plain\ttext\r\n\f\u001b"u8));
        Assert.IsFalse(FilePreviewService.LooksBinary([0xFF, 0xFE, 0, 0]));
        Assert.IsFalse(FilePreviewService.LooksBinary([0xFE, 0xFF, 0, 0]));
        Assert.IsFalse(FilePreviewService.LooksBinary([]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HexDump_ShowsOffsetsAsciiAndTheRemainder()
    {
        byte[] data = [.. Enumerable.Range(0, 20).Select(i => (byte)(0x41 + i)), 0x00, 0x7F];

        FilePreview small = FilePreviewService.BuildHexDump("f.bin", 22, data);
        FilePreview large = FilePreviewService.BuildHexDump("f.bin", 100_000, data);

        StringAssert.StartsWith(small.Lines[0], "00000000  41 42");
        Assert.IsTrue(small.Lines[1].StartsWith("00000010"));
        Assert.IsTrue(small.Lines[1].EndsWith("."), "non-printable bytes are dots");
        Assert.IsTrue(large.Lines.Any(l => l.Contains("more bytes")));
    }
}
