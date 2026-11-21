using System.Text;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Services;

/// <summary>
/// Every Office family the viewer reads, in each of its variants (templates, macro-enabled, slide shows, the 97–2003
/// binaries and the flat OpenDocument files), read through <see cref="FileToTextConverterService"/> and compared with
/// the adjacent <c>.expected.txt</c>. The expected text is checked against a key line first, so a file that comes back
/// empty or as a "could not read" note fails even before the expected file is consulted.
/// </summary>
[TestClass]
public class OfficeVariantFixtureTest
{
    private static readonly string TestFilesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Services", "TestFiles");

    private const string WordText = "Sample Word Content";
    private const string SheetText = "Sample Spreadsheet Content";
    private const string SlideText = "Sample Presentation Content";
    private const string DrawingText = "Start node";

    private static async Task<IReadOnlyList<string>> ConvertFileAsync(string fileName)
    {
        var textModel = new TextModel();
        await new FileToTextConverterService().ReadFileAsync(
            new Url(Path.Combine(TestFilesDir, fileName)), new LocalFileSystemAdapter(), textModel);
        return [.. textModel.Lines];
    }

    private static IReadOnlyList<string> GetOrCreateExpected(string fileName, IReadOnlyList<string> actual)
    {
        var expectedPath = Path.Combine(TestFilesDir, fileName + ".expected.txt");
        if (!File.Exists(expectedPath))
        {
            File.WriteAllLines(expectedPath, actual, Encoding.UTF8);
        }

        return File.ReadAllLines(expectedPath, Encoding.UTF8);
    }

    private static void AssertReadWithoutError(IReadOnlyList<string> lines, string key, string file)
    {
        Assert.IsFalse(lines.Any(l => l.StartsWith("[Could not read", StringComparison.Ordinal)),
            $"[{file}] The reader reported a failure: {string.Join(" | ", lines.Take(3))}");
        Assert.IsTrue(lines.Any(l => l.Contains(key, StringComparison.Ordinal)),
            $"[{file}] Expected '{key}' in the output, got: {string.Join(" | ", lines.Take(5))}");
    }

    private static void AssertLinesMatch(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string fileName)
    {
        Assert.AreEqual(expected.Count, actual.Count,
            $"[{fileName}] Line count mismatch. Expected {expected.Count}, got {actual.Count}.");

        for (int i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected[i], actual[i],
                $"[{fileName}] Line {i + 1} mismatch.\n  Expected: {expected[i]}\n  Actual:   {actual[i]}");
        }
    }

    // ── Word: the Open XML and OpenDocument text families, the 97–2003 binary and the flat XML ──

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("sample.docx", WordText)]
    [DataRow("sample.docm", WordText)]
    [DataRow("sample.dotx", WordText)]
    [DataRow("sample.dotm", WordText)]
    [DataRow("sample.doc", WordText)]
    [DataRow("sample.dot", WordText)]
    [DataRow("sample.odt", WordText)]
    [DataRow("sample.ott", WordText)]
    [DataRow("sample.fodt", WordText)]
    public async Task ConvertWordVariant_MatchesExpected(string file, string key)
    {
        var actual = await ConvertFileAsync(file);

        AssertReadWithoutError(actual, key, file);
        AssertLinesMatch(GetOrCreateExpected(file, actual), actual, file);
    }

    // ── Excel ───────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("sample.xlsx", SheetText)]
    [DataRow("sample.xlsm", SheetText)]
    [DataRow("sample.xltx", SheetText)]
    [DataRow("sample.xltm", SheetText)]
    [DataRow("sample.xls", SheetText)]
    [DataRow("sample.xlt", SheetText)]
    [DataRow("sample.ods", SheetText)]
    [DataRow("sample.ots", SheetText)]
    [DataRow("sample.fods", SheetText)]
    public async Task ConvertSpreadsheetVariant_MatchesExpected(string file, string key)
    {
        var actual = await ConvertFileAsync(file);

        AssertReadWithoutError(actual, key, file);
        AssertLinesMatch(GetOrCreateExpected(file, actual), actual, file);
    }

    // ── PowerPoint ──────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("sample.pptx", SlideText)]
    [DataRow("sample.pptm", SlideText)]
    [DataRow("sample.potx", SlideText)]
    [DataRow("sample.potm", SlideText)]
    [DataRow("sample.ppsx", SlideText)]
    [DataRow("sample.ppsm", SlideText)]
    [DataRow("sample.ppt", SlideText)]
    [DataRow("sample.pot", SlideText)]
    [DataRow("sample.pps", SlideText)]
    [DataRow("sample.odp", SlideText)]
    [DataRow("sample.otp", SlideText)]
    [DataRow("sample.fodp", SlideText)]
    public async Task ConvertPresentationVariant_MatchesExpected(string file, string key)
    {
        var actual = await ConvertFileAsync(file);

        AssertReadWithoutError(actual, key, file);
        AssertLinesMatch(GetOrCreateExpected(file, actual), actual, file);
    }

    // ── Drawings ────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("diagram.odg", DrawingText)]
    [DataRow("sample.otg", DrawingText)]
    [DataRow("sample.fodg", DrawingText)]
    public async Task ConvertDrawingVariant_MatchesExpected(string file, string key)
    {
        var actual = await ConvertFileAsync(file);

        AssertReadWithoutError(actual, key, file);
        AssertLinesMatch(GetOrCreateExpected(file, actual), actual, file);
    }

    // ── The same bytes on a drive that has no local path ────────────────────

    /// <summary>
    /// A document on an SFTP, SMB or archive drive is read through the file system, never by its path. The file system
    /// here serves the bytes of a sample for a path that does not exist on disk, so the read cannot be satisfied by
    /// opening the file itself.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("sample.docx")]
    [DataRow("sample.dotm")]
    [DataRow("sample.xlsm")]
    [DataRow("sample.xltx")]
    [DataRow("sample.pptx")]
    [DataRow("sample.ppsx")]
    [DataRow("sample.odt")]
    [DataRow("sample.ods")]
    [DataRow("sample.fodp")]
    [DataRow("sample.otg")]
    public async Task ConvertVariant_FromBytesOnAnotherDrive_MatchesLocalRead(string file)
    {
        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(TestFilesDir, file));
        Mock<IMemoryFile> memoryFile = new();
        memoryFile.Setup(f => f.Content).Returns(bytes);

        Mock<IFileSystem> remote = new();
        remote.Setup(f => f.ExistsFile(It.IsAny<Url>())).Returns(true);
        remote.Setup(f => f.ReadFileAsync(It.IsAny<Url>())).ReturnsAsync(memoryFile.Object);

        var textModel = new TextModel();
        await new FileToTextConverterService().ReadFileAsync(
            new Url($"/not/on/this/disk/{file}"), remote.Object, textModel);

        IReadOnlyList<string> local = await ConvertFileAsync(file);
        CollectionAssert.AreEqual(local.ToList(), textModel.Lines.ToList(), $"[{file}] remote read differs from local read.");
    }

    // ── Every example in Test/FileTypes ─────────────────────────────────────

    /// <summary>
    /// The one-per-format examples in the repository's <c>Test/FileTypes</c> folder, each holding a single line that names
    /// its own format. Each is read and must show that line, so a broken example or a regressed reader fails here.
    /// </summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task ConvertFile_ExamplesInTestFileTypes_ShowTheirOwnLine()
    {
        string examples = FindTestFileTypesFolder();
        string[] extensions = ["docx", "docm", "xlsx", "xlsm", "pptx", "pptm", "odt", "ods", "odp"];

        foreach (string extension in extensions)
        {
            string file = Path.Combine(examples, "example." + extension);
            var textModel = new TextModel();
            await new FileToTextConverterService().ReadFileAsync(new Url(file), new LocalFileSystemAdapter(), textModel);

            AssertReadWithoutError(textModel.Lines.ToList(), "iTerminal example " + extension, "example." + extension);
        }
    }

    private static string FindTestFileTypesFolder()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Test", "FileTypes");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        Assert.Inconclusive("The Test/FileTypes folder was not found above the test output folder.");
        return string.Empty;
    }
}
