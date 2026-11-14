using System.Text;
using iConsole.Fonts;
using iConsole.Services;
using iConsole.Test.Common;
using iConsole.Test.Services;

namespace iConsole.Test.Fonts;

[TestClass]
public class FontFileReaderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Read_ATrueTypeFontReadsItsNamesAndMetrics()
    {
        FontInfo info = FontFileReader.Read(SampleFiles.Bytes("example.ttf"));

        Assert.AreEqual("TrueType", info.Outlines);
        Assert.AreEqual("Sample Sans", info.FamilyName);
        Assert.AreEqual("Regular", info.SubfamilyName);
        Assert.AreEqual("Sample Sans Regular", info.FullName);
        Assert.AreEqual("SampleSans-Regular", info.PostScriptName);
        Assert.AreEqual("Version 1.0", info.Version);
        StringAssert.StartsWith(info.Copyright, "Generated for iTerminal tests");
        Assert.AreEqual(1000, info.UnitsPerEm);
        Assert.AreEqual(4, info.GlyphCount);
        Assert.AreEqual(3, info.CharacterCount, "the map covers the space, A and B");
        Assert.AreEqual(400, info.Weight);
        Assert.AreEqual(5, info.WidthClass);
        Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), info.Created);
        Assert.AreEqual(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), info.Modified);
        Assert.AreEqual("installable", info.Embedding);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_AnOpenTypeFontWithCffOutlinesReadsTheSameNames()
    {
        FontInfo info = FontFileReader.Read(SampleFiles.Bytes("example.otf"));

        Assert.AreEqual("OpenType (CFF)", info.Outlines);
        Assert.AreEqual("Sample Sans", info.FamilyName);
        Assert.AreEqual("SampleSans-Regular", info.PostScriptName);
        Assert.AreEqual(4, info.GlyphCount);
        Assert.AreEqual(3, info.CharacterCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_ANonFontIsRefused()
    {
        Assert.ThrowsExactly<FormatException>(() => FontFileReader.Read(Encoding.ASCII.GetBytes("this is not a font file")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_AFontWhoseTableDirectoryIsCutShortIsRefused()
    {
        byte[] full = SampleFiles.Bytes("example.ttf");

        Assert.ThrowsExactly<FormatException>(() => FontFileReader.Read(full[..12]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_TheExampleFontLoadsForAPreview()
    {
        Assert.IsNotNull(FileToTextConverterService.RenderFontSample(SampleFiles.Bytes("example.ttf")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheViewerShowsTheNamesMetricsAndAPreview()
    {
        var textModel = new iConsole.Model.TextModel();

        await new FileToTextConverterService().ReadFileAsync(
            new iConsole.Data.Url(SampleFiles.Path("example.ttf")), new LocalFileSystemAdapter(), textModel);

        List<string> lines = textModel.Lines.ToList();
        Assert.IsTrue(lines.Any(l => l.Contains("Names")), "the names section has a heading");
        Assert.IsTrue(lines.Any(l => l.Contains("Sample Sans")), "the family name is shown");
        Assert.IsTrue(lines.Any(l => l.Trim().StartsWith("Glyphs") && l.Trim().EndsWith("4")), "the glyph count is shown");
        Assert.IsTrue(lines.Any(l => l.Any(c => c is '█' or '▓' or '▒' or '░')), "the preview is drawn as text art");
    }
}
