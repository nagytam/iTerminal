using System.IO.Compression;
using System.Text;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Documents;
using iTerminal.Core.Documents.Extractors;

namespace iTerminal.Core.Test.Documents;

/// <summary>
/// Every Office variant the index reads: templates, macro-enabled files, slide shows, the 97–2003 binaries and the flat
/// OpenDocument files. Each is claimed by an extractor, classed as an Office document, and yields its text.
/// </summary>
[TestClass]
public class OfficeVariantExtractorTest
{
    private static readonly string[] AllOfficeVariants =
    [
        ".docx", ".docm", ".dotx", ".dotm", ".doc", ".dot", ".rtf",
        ".xlsx", ".xlsm", ".xltx", ".xltm", ".xls", ".xlt",
        ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm", ".ppt", ".pot", ".pps",
        ".odt", ".ott", ".fodt", ".ods", ".ots", ".fods", ".odp", ".otp", ".fodp", ".odg", ".otg", ".fodg",
    ];

    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "OfficeVariantTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
        }
    }

    private string WritePackage(string fileName, params (string Part, string Xml)[] parts)
    {
        string path = Path.Combine(_folder, fileName);
        using FileStream stream = File.Create(path);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        foreach ((string part, string xml) in parts)
        {
            using StreamWriter writer = new(archive.CreateEntry(part).Open(), Encoding.UTF8);
            writer.Write(xml);
        }

        return path;
    }

    private static string RepositoryFile(string relativePath)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        Assert.Inconclusive($"{relativePath} was not found above the test output folder.");
        return string.Empty;
    }

    private static readonly DocumentTextExtractorFactory Factory = new();

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(".docx")]
    [DataRow(".docm")]
    [DataRow(".dotx")]
    [DataRow(".dotm")]
    [DataRow(".doc")]
    [DataRow(".dot")]
    [DataRow(".rtf")]
    [DataRow(".xlsx")]
    [DataRow(".xlsm")]
    [DataRow(".xltx")]
    [DataRow(".xltm")]
    [DataRow(".xls")]
    [DataRow(".xlt")]
    [DataRow(".pptx")]
    [DataRow(".pptm")]
    [DataRow(".potx")]
    [DataRow(".potm")]
    [DataRow(".ppsx")]
    [DataRow(".ppsm")]
    [DataRow(".ppt")]
    [DataRow(".pot")]
    [DataRow(".pps")]
    [DataRow(".odt")]
    [DataRow(".ott")]
    [DataRow(".fodt")]
    [DataRow(".ods")]
    [DataRow(".ots")]
    [DataRow(".fods")]
    [DataRow(".odp")]
    [DataRow(".otp")]
    [DataRow(".fodp")]
    [DataRow(".odg")]
    [DataRow(".otg")]
    [DataRow(".fodg")]
    public void EveryVariant_IsClaimedByAnExtractor_AndKindedAsOffice(string extension)
    {
        string path = Path.Combine(_folder, "file" + extension);

        Assert.IsTrue(Factory.IsSupported(path), $"No extractor claims {extension}.");
        Assert.AreEqual(DocumentKind.Office, DocumentTextExtractorFactory.GetKind(path), $"{extension} is not an Office document.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VariantList_MatchesTheOfficeExtensionsTheIndexKnows()
    {
        // Guards the list above against drifting from the formats the test actually exercises.
        foreach (string extension in AllOfficeVariants)
        {
            Assert.IsTrue(Factory.SupportedExtensions.Contains(extension), $"{extension} is missing from the index.");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(".dotx", "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Template words</w:t></w:r></w:p></w:body></w:document>")]
    [DataRow(".dotm", "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Template words</w:t></w:r></w:p></w:body></w:document>")]
    [DataRow(".xltx", "xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Template words</t></is></c></row></sheetData></worksheet>")]
    [DataRow(".xltm", "xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Template words</t></is></c></row></sheetData></worksheet>")]
    [DataRow(".potx", "ppt/slides/slide1.xml", "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Template words</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>")]
    [DataRow(".ppsm", "ppt/slides/slide1.xml", "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Template words</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>")]
    public void OpenXmlVariant_ExtractsItsTextFromTheSamePartAsItsBaseFormat(string extension, string part, string xml)
    {
        string path = WritePackage("sample" + extension, (part, xml));

        DocumentTextResult result = new OfficeTextExtractor().Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status);
        StringAssert.Contains(result.Text, "Template words");
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(".ott")]
    [DataRow(".ots")]
    [DataRow(".otp")]
    [DataRow(".otg")]
    public void OpenDocumentTemplate_ExtractsItsContentXml(string extension)
    {
        string path = WritePackage(
            "sample" + extension,
            ("content.xml", "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\"><office:body><text:p>Template words</text:p></office:body></office:document-content>"));

        DocumentTextResult result = new OfficeTextExtractor().Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status);
        StringAssert.Contains(result.Text, "Template words");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FlatOpenDocumentText_IsReadAsItsOwnXml()
    {
        // A flat .fodt is the content XML itself, with no zip around it.
        string path = Path.Combine(_folder, "flat.fodt");
        File.WriteAllText(path,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\">" +
            "<office:body><office:text><text:p>Flat words</text:p></office:text></office:body></office:document>");

        DocumentTextResult result = new BinaryOfficeTextExtractor().Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status);
        StringAssert.Contains(result.Text, "Flat words");
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("sample.doc", "Sample Word Content")]
    [DataRow("sample.dot", "Sample Word Content")]
    [DataRow("sample.rtf", "Sample Word Content")]
    [DataRow("sample.xls", "Sample Spreadsheet Content")]
    [DataRow("sample.xlt", "Sample Spreadsheet Content")]
    [DataRow("sample.ppt", "Sample Presentation Content")]
    [DataRow("sample.pot", "Sample Presentation Content")]
    [DataRow("sample.pps", "Sample Presentation Content")]
    public void Legacy97To2003Binary_ExtractsTheTextOfTheSampleFile(string fileName, string expected)
    {
        // The iConsole fixture files are the same documents the viewer is checked against.
        string source = RepositoryFile(Path.Combine("iConsole.Test", "Services", "TestFiles", fileName));
        string copy = Path.Combine(_folder, fileName);
        File.Copy(source, copy);

        DocumentTextResult result = new BinaryOfficeTextExtractor().Extract(copy);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status, result.Note);
        StringAssert.Contains(result.Text, expected);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("bad.doc")]
    [DataRow("bad.xls")]
    [DataRow("bad.ppt")]
    [DataRow("bad.fodt")]
    public void DamagedBinaryOrFlatFile_IsNotIndexedAsText(string fileName)
    {
        string path = Path.Combine(_folder, fileName);
        File.WriteAllText(path, "this is not an Office document at all");

        DocumentTextResult result = new BinaryOfficeTextExtractor().Extract(path);

        Assert.AreNotEqual(DocumentExtractionStatus.Extracted, result.Status,
            $"A damaged {fileName} was indexed as text: {result.Text}");
    }
}
