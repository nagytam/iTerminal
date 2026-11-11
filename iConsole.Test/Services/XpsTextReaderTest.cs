using System.IO.Compression;
using System.Text;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading an XPS package: the sequence's documents, their pages in order, and each page's glyph runs as lines.</summary>
[TestClass]
public class XpsTextReaderTest
{
    private const string Namespace = "http://schemas.microsoft.com/xps/2005/06";

    private static byte[] MakeXps(params (string Name, string Xml)[] parts)
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string xml) in parts)
            {
                using StreamWriter writer = new(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(xml);
            }
        }

        return buffer.ToArray();
    }

    private static byte[] Package() => MakeXps(
        ("FixedDocumentSequence.fdseq", $"""
            <FixedDocumentSequence xmlns="{Namespace}">
              <DocumentReference Source="Documents/1/FixedDocument.fdoc"/>
            </FixedDocumentSequence>
            """),
        ("Documents/1/FixedDocument.fdoc", $"""
            <FixedDocument xmlns="{Namespace}">
              <PageContent Source="Pages/2.fpage"/>
              <PageContent Source="/Documents/1/Pages/1.fpage"/>
            </FixedDocument>
            """),
        ("Documents/1/Pages/1.fpage", $"""
            <FixedPage xmlns="{Namespace}">
              <Glyphs OriginX="60" OriginY="50" UnicodeString="World"/>
              <Glyphs OriginX="10" OriginY="50" UnicodeString="Hello"/>
              <Glyphs OriginX="10" OriginY="80" UnicodeString="Next line"/>
            </FixedPage>
            """),
        ("Documents/1/Pages/2.fpage", $"""
            <FixedPage xmlns="{Namespace}">
              <Glyphs OriginX="10" OriginY="50" UnicodeString="Referenced first"/>
            </FixedPage>
            """));

    [Timeout(60_000)]
    [TestMethod]
    public void Pages_FollowTheDocumentsOrder_NotTheirFileNames()
    {
        List<string> lines = [.. XpsTextReader.Read(Package())];

        int first = lines.IndexOf("Referenced first");
        int second = lines.FindIndex(l => l.StartsWith("Hello"));
        Assert.IsTrue(first >= 0 && second > first, string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.StartsWith("── Page 1")), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GlyphRuns_OnOneLine_ReadLeftToRight_AndLinesFollowTheirOrigin()
    {
        List<string> lines = [.. XpsTextReader.Read(Package())];

        CollectionAssert.Contains(lines, "Hello World");
        CollectionAssert.Contains(lines, "Next line");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APackageWithoutASequence_TheReaderSaysSo()
    {
        List<string> lines = [.. XpsTextReader.Read(MakeXps(("Other.xml", "<x/>")))];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the document:"), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARealPackage_NamesItsSequenceThroughTheRootRelationship()
    {
        byte[] content = MakeXps(
            ("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="R1" Type="http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" Target="/FixedDocSeq.fdseq"/>
                </Relationships>
                """),
            ("FixedDocSeq.fdseq", $"""
                <FixedDocumentSequence xmlns="{Namespace}">
                  <DocumentReference Source="Documents/1/FixedDocument.fdoc"/>
                </FixedDocumentSequence>
                """),
            ("Documents/1/FixedDocument.fdoc", $"""
                <FixedDocument xmlns="{Namespace}">
                  <PageContent Source="Pages/1.fpage"/>
                </FixedDocument>
                """),
            ("Documents/1/Pages/1.fpage", $"""
                <FixedPage xmlns="{Namespace}" Width="816" Height="1056">
                  <Glyphs OriginX="10" OriginY="50" UnicodeString="From the relationship"/>
                </FixedPage>
                """));

        List<string> lines = [.. XpsTextReader.Read(content)];

        CollectionAssert.Contains(lines, "From the relationship");
        Assert.IsFalse(lines.Any(l => l.StartsWith("[Could not read the document:")), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnOpenXpsPackage_InItsOwnNamespace_ReadsTheSame()
    {
        const string openXpsNamespace = "http://schemas.openxps.org/oxps/v1.0";
        byte[] content = MakeXps(
            ("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="R1" Type="http://schemas.openxps.org/oxps/v1.0/fixedrepresentation" Target="/FixedDocSeq.fdseq"/>
                </Relationships>
                """),
            ("FixedDocSeq.fdseq", $"""
                <FixedDocumentSequence xmlns="{openXpsNamespace}">
                  <DocumentReference Source="Documents/1/FixedDocument.fdoc"/>
                </FixedDocumentSequence>
                """),
            ("Documents/1/FixedDocument.fdoc", $"""
                <FixedDocument xmlns="{openXpsNamespace}">
                  <PageContent Source="Pages/1.fpage"/>
                </FixedDocument>
                """),
            ("Documents/1/Pages/1.fpage", $"""
                <FixedPage xmlns="{openXpsNamespace}">
                  <Glyphs OriginX="10" OriginY="50" UnicodeString="OpenXPS page"/>
                </FixedPage>
                """));

        List<string> lines = [.. XpsTextReader.Read(content)];

        CollectionAssert.Contains(lines, "OpenXPS page");
        Assert.IsTrue(lines.Any(l => l.StartsWith("── Page 1")), string.Join("\n", lines));
    }
}
