using System.IO.Compression;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading an OpenDocument presentation (.odp/.otp) into the same framed slides a PowerPoint file gets.</summary>
[TestClass]
public class OpenDocumentPresentationTest
{
    private const string Header = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content
            xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
            xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
            xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
            xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0">
          <office:body><office:presentation>
        """;

    private const string Footer = "</office:presentation></office:body></office:document-content>";

    private static byte[] MakeOdp(string pages)
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using StreamWriter writer = new(zip.CreateEntry("content.xml").Open());
            writer.Write(Header + pages + Footer);
        }

        return buffer.ToArray();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASlide_HasItsTitleAsHeading_AndItsOutlineAsBullets()
    {
        byte[] odp = MakeOdp("""
            <draw:page draw:name="page1">
              <draw:frame presentation:class="title"><draw:text-box><text:p>Plan</text:p></draw:text-box></draw:frame>
              <draw:frame presentation:class="outline"><draw:text-box><text:p>Ship it</text:p></draw:text-box></draw:frame>
            </draw:page>
            """);

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentPresentation(odp)];

        Assert.IsTrue(lines[0].StartsWith("── Slide 1: Plan"), string.Join("\n", lines));
        Assert.AreEqual("  • Ship it", lines[1]);
        Assert.IsFalse(lines.Skip(1).Any(l => l.Contains("Plan")), "The title should not repeat in the body.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SpeakerNotes_FollowTheSlideIndented()
    {
        byte[] odp = MakeOdp("""
            <draw:page draw:name="page1">
              <draw:frame presentation:class="title"><draw:text-box><text:p>Talk</text:p></draw:text-box></draw:frame>
              <presentation:notes>
                <draw:frame presentation:class="notes"><draw:text-box><text:p>Pause here.</text:p></draw:text-box></draw:frame>
              </presentation:notes>
            </draw:page>
            """);

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentPresentation(odp)];

        int notes = lines.IndexOf("  Notes:");
        Assert.IsTrue(notes > 0, string.Join("\n", lines));
        Assert.AreEqual("    Pause here.", lines[notes + 1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListItems_KeepTheirDepth()
    {
        byte[] odp = MakeOdp("""
            <draw:page draw:name="page1">
              <draw:frame presentation:class="outline"><draw:text-box>
                <text:list><text:list-item><text:p>Top</text:p>
                  <text:list><text:list-item><text:p>Nested</text:p></text:list-item></text:list>
                </text:list-item></text:list>
              </draw:text-box></draw:frame>
            </draw:page>
            """);

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentPresentation(odp)];

        CollectionAssert.Contains(lines, "  • Top");
        CollectionAssert.Contains(lines, "    • Nested");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AWithoutContentXml_TheRendererSaysSo()
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("styles.xml");
        }

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentPresentation(buffer.ToArray())];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the presentation:"), string.Join("\n", lines));
    }
}
