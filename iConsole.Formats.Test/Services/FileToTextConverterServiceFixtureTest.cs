using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using System.Text;

namespace iConsole.Test.Services;

/// <summary>
/// Integration tests that run <see cref="FileToTextConverterService"/> against real sample files
/// located in the TestFiles folder. The extracted text is compared to the content of the
/// adjacent <c>.expected.txt</c> file. On the first run the expected file is generated
/// automatically, so the extracted content is also stored next to the original file.
/// </summary>
[TestClass]
public class FileToTextConverterServiceFixtureTest
{
    private static readonly string TestFilesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Services", "TestFiles");

    private static FileToTextConverterService CreateService() => new();
    private static LocalFileSystemAdapter CreateFileSystem() => new();

    // ── helpers ─────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<string>> ConvertFileAsync(string fileName)
    {
        var filePath = Path.Combine(TestFilesDir, fileName);
        var url = new Url(filePath);
        var textModel = new TextModel();
        await CreateService().ReadFileAsync(url, CreateFileSystem(), textModel);
        return [.. textModel.Lines];
    }

    /// <summary>
    /// Writes the extracted lines to the expected file if it does not yet exist,
    /// then reads it back for comparison.
    /// </summary>
    private static IReadOnlyList<string> GetOrCreateExpected(string fileName, IReadOnlyList<string> actual)
    {
        var expectedPath = Path.Combine(TestFilesDir, fileName + ".expected.txt");
        if (!File.Exists(expectedPath))
        {
            File.WriteAllLines(expectedPath, actual, Encoding.UTF8);
        }

        var lines = File.ReadAllLines(expectedPath, Encoding.UTF8);
        return lines;
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

    // ── text ────────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Txt_MatchesExpected()
    {
        const string file = "sample.txt";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    // ── HTML ────────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Html_MatchesExpected()
    {
        const string file = "sample.html";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");
        Assert.IsTrue(actual.Any(l => l.Contains("Welcome")), $"[{file}] Expected 'Welcome' in output.");
        Assert.IsFalse(actual.Any(l => l.Contains("should be stripped")), $"[{file}] Script content should be stripped.");
        Assert.IsFalse(actual.Any(l => l.Contains("color: red")), $"[{file}] Style content should be stripped.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Htm_MatchesExpected()
    {
        const string file = "sample.htm";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");
        Assert.IsTrue(actual.Any(l => l.Contains("HTM File")), $"[{file}] Expected 'HTM File' in output.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    // ── PDF ─────────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Pdf_ContainsExpectedText()
    {
        const string file = "sample.pdf";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");

        var joined = string.Join(" ", actual);
        Assert.IsTrue(joined.Contains("Sample PDF Content"), $"[{file}] Expected 'Sample PDF Content' in output.");

        GetOrCreateExpected(file, actual);
    }

    // ── images ──────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Bmp_ProducesAsciiArt()
    {
        await AssertImageConversionAsync("sample.bmp");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Png_ProducesAsciiArt()
    {
        await AssertImageConversionAsync("sample.png");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Jpg_ProducesAsciiArt()
    {
        await AssertImageConversionAsync("sample.jpg");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Gif_ProducesAsciiArt()
    {
        await AssertImageConversionAsync("sample.gif");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Tiff_ProducesAsciiArt()
    {
        await AssertImageConversionAsync("sample.tiff");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Tif_ProducesAsciiArt()
    {
        // Same bytes as sample.tiff — "tif" is just an accepted extension alias.
        await AssertImageConversionAsync("sample.tif");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Jpeg_ProducesAsciiArt()
    {
        // Same bytes as sample.jpg — "jpeg" is just an accepted extension alias.
        await AssertImageConversionAsync("sample.jpeg");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Webp_ProducesAsciiArt()
    {
        await AssertImageConversionAsync("sample.webp");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Ico_ProducesAsciiArt()
    {
        await AssertImageConversionAsync("sample.ico");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Cur_ProducesAsciiArt()
    {
        await AssertImageConversionAsync("sample.cur");
    }

    private static async Task AssertImageConversionAsync(string file)
    {
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");
        Assert.IsTrue(actual.All(l => l.Length > 0), $"[{file}] All lines should be non-empty.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    // ── Word ────────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Docx_MatchesExpected()
    {
        const string file = "sample.docx";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");
        Assert.IsTrue(actual.Any(l => l.Contains("Sample Word Content")), $"[{file}] Expected 'Sample Word Content' in output.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    // ── Excel ───────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Xlsx_MatchesExpected()
    {
        await AssertSpreadsheetConversionAsync("sample.xlsx");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Xlsm_MatchesExpected()
    {
        // Same OOXML content as sample.xlsx — "xlsm" is just an accepted extension alias.
        await AssertSpreadsheetConversionAsync("sample.xlsm");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Xltx_MatchesExpected()
    {
        // Same OOXML content as sample.xlsx — "xltx" is just an accepted extension alias.
        await AssertSpreadsheetConversionAsync("sample.xltx");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Xltm_MatchesExpected()
    {
        // Same OOXML content as sample.xlsx — "xltm" is just an accepted extension alias.
        await AssertSpreadsheetConversionAsync("sample.xltm");
    }

    private static async Task AssertSpreadsheetConversionAsync(string file)
    {
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");
        Assert.IsTrue(actual.Any(l => l.Contains("Sample Spreadsheet Content")), $"[{file}] Expected 'Sample Spreadsheet Content' in output.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    // ── PowerPoint ──────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Pptx_MatchesExpected()
    {
        await AssertPresentationConversionAsync("sample.pptx");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Pptm_MatchesExpected()
    {
        // Same OOXML content as sample.pptx — "pptm" is just an accepted extension alias.
        await AssertPresentationConversionAsync("sample.pptm");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Potx_MatchesExpected()
    {
        // Same OOXML content as sample.pptx — "potx" is just an accepted extension alias.
        await AssertPresentationConversionAsync("sample.potx");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Potm_MatchesExpected()
    {
        // Same OOXML content as sample.pptx — "potm" is just an accepted extension alias.
        await AssertPresentationConversionAsync("sample.potm");
    }

    private static async Task AssertPresentationConversionAsync(string file)
    {
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");
        Assert.IsTrue(actual.Any(l => l.Contains("Sample Presentation Content")), $"[{file}] Expected 'Sample Presentation Content' in output.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    // ── Assembly metadata (dll / exe / so) ─────────────────────────────────
    // sample.dll and sample.so are the same small managed assembly (built with
    // custom AssemblyTitle/Company/Version) under different accepted extensions.
    // sample.exe is the real native apphost stub produced for that same project,
    // which is *not* a managed assembly — it exercises the BadImageFormatException
    // fallback branch ("Note: Not a managed .NET assembly").

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Dll_ShowsAssemblyMetadata()
    {
        const string file = "sample.dll";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");
        Assert.IsTrue(actual.Any(l => l.Contains("Assembly Identity")), $"[{file}] Expected an 'Assembly Identity' section.");
        Assert.IsTrue(actual.Any(l => l.Contains("SampleFixtureAssembly")), $"[{file}] Expected the assembly name in the output.");

        GetOrCreateExpected(file, actual);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_So_ShowsAssemblyMetadata()
    {
        const string file = "sample.so";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");
        Assert.IsTrue(actual.Any(l => l.Contains("Assembly Identity")), $"[{file}] Expected an 'Assembly Identity' section.");

        GetOrCreateExpected(file, actual);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Exe_ShowsFileVersionInfoAndSkipsAssemblyIdentity()
    {
        const string file = "sample.exe";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Count > 0, $"[{file}] Converter produced no output.");
        Assert.IsTrue(actual.Any(l => l.Contains("File Version Info")), $"[{file}] Expected a 'File Version Info' section.");
        Assert.IsTrue(actual.Any(l => l.Contains("Not a managed .NET assembly")),
            $"[{file}] The native apphost stub should hit the BadImageFormatException fallback, not report a managed Assembly Identity.");

        GetOrCreateExpected(file, actual);
    }

    // ── Office 97–2003 binary and OpenDocument presentations ────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Doc_MatchesExpected()
    {
        const string file = "sample.doc";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.Contains("Sample Word Content")), $"[{file}] Expected 'Sample Word Content' in output.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_RichDoc_KeepsTablesAndAccentedText()
    {
        const string file = "rich.doc";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.Contains("Árvíztűrő tükörfúrógép")), $"[{file}] Expected the accented heading.");
        Assert.IsTrue(actual.Any(l => l.Contains("│ Name") && l.Contains("│ Qty")), $"[{file}] Expected the table as a grid.");
        Assert.IsTrue(actual.Any(l => l.Contains("│ Apple") && l.Contains("│ 3")), $"[{file}] Expected the table's second row.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Xls_MatchesExpected()
    {
        const string file = "sample.xls";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.Contains("Sample Spreadsheet Content")), $"[{file}] Expected 'Sample Spreadsheet Content' in output.");
        Assert.IsTrue(actual.Any(l => l.Contains("│ 2 │ Row2Col1")), $"[{file}] Expected the second row as a numbered grid row.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_BookXls_ShowsEverySheetWithDatesAndErrors()
    {
        const string file = "book.xls";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.Contains("── Budget")), $"[{file}] Expected the first sheet's heading.");
        Assert.IsTrue(actual.Any(l => l.Contains("── Notes")), $"[{file}] Expected the second sheet's heading.");
        Assert.IsTrue(actual.Any(l => l.Contains("2026-01-02")), $"[{file}] A date-formatted cell should show as a date.");
        Assert.IsTrue(actual.Any(l => l.Contains("#VALUE!")), $"[{file}] A stored error should show as the error.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Ppt_MatchesExpected()
    {
        const string file = "sample.ppt";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.Contains("Sample Presentation Content")), $"[{file}] Expected the slide text.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_DeckPpt_ShowsSlidesTitlesAndNotes()
    {
        const string file = "deck.ppt";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.StartsWith("── Slide 1: Quarterly Résumé")), $"[{file}] Expected slide 1 with its title.");
        Assert.IsTrue(actual.Any(l => l.StartsWith("── Slide 2: Figures")), $"[{file}] Expected slide 2 with its title.");
        Assert.IsTrue(actual.Any(l => l.Contains("Remember to thank the team.")), $"[{file}] Expected the speaker notes.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Odp_MatchesExpected()
    {
        const string file = "sample.odp";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.Contains("Sample Presentation Content")), $"[{file}] Expected the slide text.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Otp_MatchesExpected()
    {
        const string file = "sample.otp";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.Contains("Sample Presentation Content")), $"[{file}] Expected the slide text.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_DeckOdp_ShowsSlidesTitlesTablesAndNotes()
    {
        const string file = "deck.odp";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.StartsWith("── Slide 1: Quarterly Résumé")), $"[{file}] Expected slide 1 with its title.");
        Assert.IsTrue(actual.Any(l => l == "  • First point & more"), $"[{file}] List items should be bullets.");
        Assert.IsTrue(actual.Any(l => l.Contains("│ Region │ Sales │")), $"[{file}] Expected the slide table as a grid.");
        Assert.IsTrue(actual.Any(l => l.Contains("Remember to thank the team.")), $"[{file}] Expected the speaker notes.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    // ── RTF and EPUB ────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Rtf_MatchesExpected()
    {
        const string file = "sample.rtf";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.Contains("Sample Word Content")), $"[{file}] Expected 'Sample Word Content' in output.");
        Assert.IsFalse(actual.Any(l => l.Contains("\\par") || l.Contains("{")), $"[{file}] Control words should not show.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_EscapesRtf_DecodesCodePagesAndUnicode()
    {
        const string file = "escapes.rtf";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.StartsWith("Café au lait — €")), $"[{file}] Expected the escaped text decoded.");
        Assert.IsTrue(actual.Any(l => l == "€two-byte"), $"[{file}] uc2 should skip two fallback units.");
        Assert.IsTrue(actual.Any(l => l == "First item"), $"[{file}] List numbering should not show.");
        Assert.IsTrue(actual.Any(l => l.Contains("Field: linked text")), $"[{file}] A field shows its result, not its instruction.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Epub_MatchesExpected()
    {
        const string file = "sample.epub";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.Contains("Sample Word Content")), $"[{file}] Expected 'Sample Word Content' in output.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_BookEpub_ReadsChaptersInSpineOrder()
    {
        const string file = "book.epub";
        var actual = await ConvertFileAsync(file);

        int one = actual.ToList().FindIndex(l => l == "# Chapter One");
        int second = actual.ToList().FindIndex(l => l == "## Second");
        Assert.IsTrue(one >= 0 && second > one, $"[{file}] Chapters should come in spine order.");
        Assert.IsFalse(actual.Any(l => l.Contains("var x")), $"[{file}] Script content should be dropped.");
        Assert.IsFalse(actual.Any(l => l.Contains("not in spine")), $"[{file}] The navigation document is not in the spine.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    // ── Drawings, mail, XPS and e-books ─────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Odg_MatchesExpected()
    {
        const string file = "diagram.odg";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Any(l => l.StartsWith("── Page 2")), $"[{file}] Expected a section per page.");
        Assert.IsTrue(actual.Any(l => l.Contains("Only page two")), $"[{file}] Expected the second page's text.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Eml_MatchesExpected()
    {
        const string file = "sample.eml";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Contains("Subject: Sample message"), $"[{file}] Expected the subject.");
        Assert.IsTrue(actual.Contains("Sample message body."), $"[{file}] Expected the body.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConvertFile_Fb2_MatchesExpected()
    {
        const string file = "sample.fb2";
        var actual = await ConvertFileAsync(file);

        Assert.IsTrue(actual.Contains("## Sample Chapter"), $"[{file}] Expected the chapter heading.");
        Assert.IsTrue(actual.Contains("Sample FictionBook content."), $"[{file}] Expected the paragraph.");

        var expected = GetOrCreateExpected(file, actual);
        AssertLinesMatch(expected, actual, file);
    }
}
