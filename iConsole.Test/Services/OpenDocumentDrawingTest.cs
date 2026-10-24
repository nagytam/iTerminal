using System.IO.Compression;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading an OpenDocument drawing (.odg/.otg): one section per page, with the text of its shapes.</summary>
[TestClass]
public class OpenDocumentDrawingTest
{
    private static byte[] MakeOdg(string pages)
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using StreamWriter writer = new(zip.CreateEntry("content.xml").Open());
            writer.Write($"""
                <?xml version="1.0" encoding="UTF-8"?>
                <office:document-content
                    xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                    xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
                    xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
                    xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0">
                  <office:body><office:drawing>{pages}</office:drawing></office:body>
                </office:document-content>
                """);
        }

        return buffer.ToArray();
    }

    [TestMethod]
    public void EachPage_IsASection_WithItsShapesText()
    {
        byte[] odg = MakeOdg("""
            <draw:page draw:name="one">
              <draw:custom-shape><text:p>Start node</text:p></draw:custom-shape>
              <draw:frame><draw:text-box><text:p>Second box</text:p></draw:text-box></draw:frame>
            </draw:page>
            <draw:page draw:name="two">
              <draw:frame><draw:text-box><text:p>Only page two</text:p></draw:text-box></draw:frame>
            </draw:page>
            """);

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentDrawing(odg)];

        int two = lines.FindIndex(l => l.StartsWith("── Page 2"));
        Assert.IsTrue(two > 0, string.Join("\n", lines));
        CollectionAssert.Contains(lines.Take(two).ToList(), "Start node");
        CollectionAssert.Contains(lines.Take(two).ToList(), "Second box");
        CollectionAssert.Contains(lines.Skip(two).ToList(), "Only page two");
    }

    [TestMethod]
    public void AWithoutContentXml_TheRendererSaysSo()
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("styles.xml");
        }

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentDrawing(buffer.ToArray())];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the drawing:"), string.Join("\n", lines));
    }
}
