using System.Text;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading a FictionBook 2 e-book: sections as headings, paragraphs, verses, tables and notes bodies.</summary>
[TestClass]
public class Fb2TextReaderTest
{
    private const string Book = """
        <?xml version="1.0" encoding="utf-8"?>
        <FictionBook xmlns="http://www.gribuzz.ru/XSD/FictionBook/2.0" xmlns:l="http://www.w3.org/1999/xlink">
          <description><title-info><book-title>Hidden title</book-title></title-info></description>
          <body>
            <title><p>The Book</p></title>
            <section>
              <title><p>Chapter One</p></title>
              <p>It was <emphasis>very</emphasis> dark.</p>
              <empty-line/>
              <poem><stanza><v>Line a</v><v>Line b</v></stanza></poem>
              <table><tr><td>cell 1</td><td>cell 2</td></tr></table>
              <section><title><p>Sub</p></title><p>Nested text.</p></section>
            </section>
          </body>
          <body name="notes">
            <section><title><p>1</p></title><p>A note.</p></section>
          </body>
          <binary id="cover.jpg" content-type="image/jpeg">AAAA</binary>
        </FictionBook>
        """;

    [Timeout(60_000)]
    [TestMethod]
    public void SectionsBecomeHeadings_ByDepth()
    {
        List<string> lines = [.. Fb2TextReader.Read(Encoding.UTF8.GetBytes(Book))];

        CollectionAssert.Contains(lines, "# The Book");
        CollectionAssert.Contains(lines, "## Chapter One");
        CollectionAssert.Contains(lines, "### Sub");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Paragraphs_KeepTheirText_WithInlineMarkupFlattened()
    {
        List<string> lines = [.. Fb2TextReader.Read(Encoding.UTF8.GetBytes(Book))];

        CollectionAssert.Contains(lines, "It was very dark.");
        CollectionAssert.Contains(lines, "Nested text.");
        CollectionAssert.Contains(lines, "  Line a");
        CollectionAssert.Contains(lines, "  Line b");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NotesBodies_AreShownUnderTheirName_AndTheDescriptionAndBinariesAreNot()
    {
        List<string> lines = [.. Fb2TextReader.Read(Encoding.UTF8.GetBytes(Book))];

        Assert.IsTrue(lines.Any(l => l.StartsWith("── notes ")), string.Join("\n", lines));
        CollectionAssert.Contains(lines, "A note.");
        Assert.IsFalse(lines.Any(l => l.Contains("Hidden title") || l.Contains("AAAA")), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ATableRow_IsAGridRow()
    {
        List<string> lines = [.. Fb2TextReader.Read(Encoding.UTF8.GetBytes(Book))];

        Assert.IsTrue(lines.Any(l => l.Contains("cell 1") && l.Contains("cell 2")), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnotherXmlDocument_IsReportedAsNotAFictionBook()
    {
        List<string> lines = [.. Fb2TextReader.Read(Encoding.UTF8.GetBytes("<root/>"))];

        CollectionAssert.AreEqual(new[] { "[Could not read the book: the file is not a FictionBook]" }, lines);
    }
}
