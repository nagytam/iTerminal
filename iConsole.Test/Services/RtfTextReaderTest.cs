using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading the text of an RTF file: control words, escapes and the destinations that are not body text.</summary>
[TestClass]
public class RtfTextReaderTest
{
    private static List<string> Read(string rtf) => [.. RtfTextReader.Read(System.Text.Encoding.Latin1.GetBytes(rtf))];

    [Timeout(60_000)]
    [TestMethod]
    public void Paragraphs_BecomeLines_AndControlWordsDoNotShow()
    {
        List<string> lines = Read(@"{\rtf1\ansi Hello {\b bold} world\par Second\par}");

        CollectionAssert.AreEqual(new[] { "Hello bold world", "Second" }, lines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HexEscapes_DecodeWithTheDeclaredCodePage()
    {
        // 0xC0 is А in Windows-1251 (Cyrillic) and À in Windows-1252.
        List<string> lines = Read(@"{\rtf1\ansi\ansicpg1251 \'c0\'e1\par}");

        Assert.AreEqual("Аб", lines.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UnicodeEscapes_SkipTheirFallbackCharacter()
    {
        List<string> lines = Read(@"{\rtf1\uc1 \u233?x\u8364?y\par}");

        Assert.AreEqual("éx€y", lines.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FontAndColourTables_AndMetadata_AreNotShown()
    {
        List<string> lines = Read(@"{\rtf1{\fonttbl{\f0 Garamond;}}{\colortbl;\red0\green0\blue0;}{\info{\title Secret title}}Body text\par}");

        Assert.AreEqual("Body text", lines.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HeadersAndFootersAndUnknownDestinations_AreNotShown()
    {
        List<string> lines = Read(@"{\rtf1{\header Running head\par}Body{\*\unknownthing hidden}\par}");

        Assert.AreEqual("Body", lines.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EscapedBracesAndBackslashes_AreText()
    {
        List<string> lines = Read(@"{\rtf1 \{braces\} and back\\slash\par}");

        Assert.AreEqual("{braces} and back\\slash", lines.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TableCells_AreSeparatedByBars_AndRowsEndTheirLine()
    {
        List<string> lines = Read(@"{\rtf1 \trowd Name\cell Qty\cell\row \trowd Apple\cell 3\cell\row}");

        CollectionAssert.AreEqual(new[] { "Name │ Qty │", "Apple │ 3 │" }, lines);
    }
}
