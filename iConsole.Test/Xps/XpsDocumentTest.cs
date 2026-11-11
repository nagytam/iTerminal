using System.IO.Compression;
using System.Text;
using iConsole.Xps;

namespace iConsole.Test.Xps;

/// <summary>
/// Reading an XPS or OpenXPS package: the sequence named by the package's root relationship, its documents, their
/// pages in reference order, each page's size and text, and the package's core properties.
/// </summary>
[TestClass]
public class XpsDocumentTest
{
    private const string Namespace = "http://schemas.microsoft.com/xps/2005/06";
    private const string OpenXpsNamespace = "http://schemas.openxps.org/oxps/v1.0";

    private static byte[] MakePackage(params (string Name, string Xml)[] parts)
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

    private static byte[] Package(string sequence = "FixedDocSeq.fdseq", string @namespace = Namespace, bool relationship = true)
    {
        List<(string, string)> parts =
        [
            (sequence, $"""
                <FixedDocumentSequence xmlns="{@namespace}">
                  <DocumentReference Source="Documents/1/FixedDocument.fdoc"/>
                </FixedDocumentSequence>
                """),
            ("Documents/1/FixedDocument.fdoc", $"""
                <FixedDocument xmlns="{@namespace}">
                  <PageContent Source="Pages/2.fpage"/>
                  <PageContent Source="/Documents/1/Pages/1.fpage"/>
                </FixedDocument>
                """),
            ("Documents/1/Pages/1.fpage", $"""
                <FixedPage xmlns="{@namespace}" Width="816" Height="1056">
                  <Glyphs OriginX="60" OriginY="50" UnicodeString="World"/>
                  <Glyphs OriginX="10" OriginY="50" UnicodeString="Hello"/>
                  <Glyphs OriginX="10" OriginY="80" UnicodeString="Next line"/>
                </FixedPage>
                """),
            ("Documents/1/Pages/2.fpage", $"""
                <FixedPage xmlns="{@namespace}" Width="595.5" Height="842.25">
                  <Glyphs OriginX="10" OriginY="50" UnicodeString="Referenced first"/>
                </FixedPage>
                """),
            ("docProps/core.xml", """
                <cp:coreProperties
                    xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                    xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:title>Quarterly report</dc:title>
                  <cp:creator>Ada Lovelace</cp:creator>
                </cp:coreProperties>
                """),
        ];

        if (relationship)
        {
            parts.Add(("_rels/.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="R1" Type="http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" Target="/{sequence}"/>
                </Relationships>
                """));
        }

        return MakePackage([.. parts]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_ReturnsThePagesInReferenceOrder_WithTheirSizeAndText()
    {
        XpsDocument document = XpsDocument.Read(Package());

        Assert.IsTrue(document.IsValid, document.Error);
        Assert.AreEqual(2, document.Pages.Count);
        CollectionAssert.AreEqual(new[] { "Referenced first" }, document.Pages[0].Lines.ToArray());
        CollectionAssert.AreEqual(new[] { "Hello World", "Next line" }, document.Pages[1].Lines.ToArray());
        Assert.AreEqual(1, document.Pages[0].Number);
        Assert.AreEqual(2, document.Pages[1].Number);
        Assert.AreEqual(595.5, document.Pages[0].Width, 0.001);
        Assert.AreEqual(842.25, document.Pages[0].Height, 0.001);
        Assert.AreEqual(816, document.Pages[1].Width, 0.001);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_NumberPagesInsideTheirOwnDocument()
    {
        XpsDocument document = XpsDocument.Read(MakePackage(
            ("_rels/.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="R1" Type="http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" Target="/FixedDocSeq.fdseq"/>
                </Relationships>
                """),
            ("FixedDocSeq.fdseq", $"""
                <FixedDocumentSequence xmlns="{Namespace}">
                  <DocumentReference Source="Documents/1/FixedDocument.fdoc"/>
                  <DocumentReference Source="Documents/2/FixedDocument.fdoc"/>
                </FixedDocumentSequence>
                """),
            ("Documents/1/FixedDocument.fdoc", $"""
                <FixedDocument xmlns="{Namespace}">
                  <PageContent Source="Pages/1.fpage"/>
                  <PageContent Source="Pages/2.fpage"/>
                </FixedDocument>
                """),
            ("Documents/2/FixedDocument.fdoc", $"""
                <FixedDocument xmlns="{Namespace}">
                  <PageContent Source="Pages/1.fpage"/>
                </FixedDocument>
                """),
            ("Documents/1/Pages/1.fpage", $"""<FixedPage xmlns="{Namespace}"><Glyphs UnicodeString="one"/></FixedPage>"""),
            ("Documents/1/Pages/2.fpage", $"""<FixedPage xmlns="{Namespace}"><Glyphs UnicodeString="two"/></FixedPage>"""),
            ("Documents/2/Pages/1.fpage", $"""<FixedPage xmlns="{Namespace}"><Glyphs UnicodeString="three"/></FixedPage>""")));

        Assert.IsTrue(document.IsValid, document.Error);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, document.Pages.Select(page => page.Number).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 1, 2 }, document.Pages.Select(page => page.DocumentNumber).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 1 }, document.Pages.Select(page => page.PageInDocument).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_FindsTheSequenceThroughTheRootRelationship_WhateverItIsNamed()
    {
        XpsDocument document = XpsDocument.Read(Package(sequence: "FixedDocSeq.fdseq"));

        Assert.IsTrue(document.IsValid, document.Error);
        Assert.AreEqual(2, document.Pages.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_WithoutARootRelationship_FallsBackToTheSequencePart()
    {
        XpsDocument document = XpsDocument.Read(Package(relationship: false));

        Assert.IsTrue(document.IsValid, document.Error);
        Assert.AreEqual(2, document.Pages.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_AnOpenXpsPackage_ReadsTheSameAsAnXpsOne()
    {
        XpsDocument document = XpsDocument.Read(Package(@namespace: OpenXpsNamespace));

        Assert.IsTrue(document.IsValid, document.Error);
        Assert.AreEqual(2, document.Pages.Count);
        CollectionAssert.AreEqual(new[] { "Hello World", "Next line" }, document.Pages[1].Lines.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_ReturnsTheCoreProperties()
    {
        XpsDocument document = XpsDocument.Read(Package());

        Assert.AreEqual("Quarterly report", document.Title);
        Assert.AreEqual("Ada Lovelace", document.Creator);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_WithoutASequence_ReturnsAnErrorInsteadOfThrowing()
    {
        XpsDocument document = XpsDocument.Read(MakePackage(("Other.xml", "<x/>")));

        Assert.IsFalse(document.IsValid);
        Assert.AreEqual(0, document.Pages.Count);
        Assert.IsTrue(document.Error!.StartsWith("[Could not read the document:"), document.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_OfBytesThatAreNotAPackage_ReturnsAnErrorInsteadOfThrowing()
    {
        XpsDocument document = XpsDocument.Read("not a zip"u8.ToArray());

        Assert.IsFalse(document.IsValid);
        Assert.IsNotNull(document.Error);
    }

    [TestMethod]
    [DataRow("report.xps")]
    [DataRow("REPORT.XPS")]
    [DataRow("report.oxps")]
    public void IsXpsFile_AcceptsTheViewerExtensions(string path) => Assert.IsTrue(XpsFileTypes.IsXpsFile(path));

    [TestMethod]
    [DataRow("report.pdf")]
    [DataRow("report")]
    [DataRow("")]
    public void IsXpsFile_RejectsEverythingElse(string path) => Assert.IsFalse(XpsFileTypes.IsXpsFile(path));
}
