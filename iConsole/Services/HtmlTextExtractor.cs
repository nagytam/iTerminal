using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using iConsole.Helper;

namespace iConsole.Services;

/// <summary>
/// Turns HTML or XHTML into lines for the viewer: headings with hashes, list items with bullets, table cells
/// separated by bars, block elements and line breaks on lines of their own. Scripts, styles and the head are dropped.
/// Used by the EPUB, e-mail and Kindle readers, which all hold markup.
/// </summary>
public static class HtmlTextExtractor
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Stands for a space inside a preformatted block until the line is tidied: a private-use character, not whitespace.</summary>
    private const char PreservedSpace = '\uE020';

    /// <summary>Elements that start and end a line of their own.</summary>
    private const string BlockElements = "//p|//div|//section|//article|//blockquote|//pre|//tr|//li|//dt|//dd|//figcaption|//table|//ul|//ol|//dl";

    /// <summary>The lines of an HTML document held as bytes, decoded with the encoding the markup declares.</summary>
    public static IReadOnlyList<string> Lines(byte[] html) => Lines(TextEncodings.Decode(html, out _));

    /// <summary>The lines of an HTML document already decoded to text.</summary>
    public static IReadOnlyList<string> Lines(string html)
    {
        HtmlDocument document = new();
        document.LoadHtml(html);

        foreach (HtmlNode node in document.DocumentNode.SelectNodes("//script|//style|//head|//nav") ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        foreach (HtmlNode node in document.DocumentNode.SelectNodes("//h1|//h2|//h3|//h4|//h5|//h6") ?? Enumerable.Empty<HtmlNode>())
        {
            node.PrependChild(document.CreateTextNode(new string('#', node.Name[1] - '0') + " "));
        }

        foreach (HtmlNode node in document.DocumentNode.SelectNodes("//li") ?? Enumerable.Empty<HtmlNode>())
        {
            node.PrependChild(document.CreateTextNode("• "));
        }

        foreach (HtmlNode node in document.DocumentNode.SelectNodes("//td|//th") ?? Enumerable.Empty<HtmlNode>())
        {
            node.ParentNode?.InsertAfter(document.CreateTextNode(" │ "), node);
        }

        foreach (HtmlNode node in document.DocumentNode.SelectNodes("//br") ?? Enumerable.Empty<HtmlNode>())
        {
            node.ParentNode?.InsertAfter(document.CreateTextNode("\n"), node);
        }

        foreach (HtmlNode node in document.DocumentNode.SelectNodes(BlockElements) ?? Enumerable.Empty<HtmlNode>())
        {
            node.ParentNode?.InsertBefore(document.CreateTextNode("\n"), node);
            node.ParentNode?.InsertAfter(document.CreateTextNode("\n"), node);
        }

        // The spaces of a <pre> block are kept: they are swapped for a private-use character that the whitespace run
        // does not match, and swapped back once the line is tidied. Otherwise code listings lost their indentation.
        foreach (HtmlNode node in document.DocumentNode.SelectNodes("//pre") ?? Enumerable.Empty<HtmlNode>())
        {
            foreach (HtmlTextNode text in node.DescendantsAndSelf().OfType<HtmlTextNode>())
            {
                text.Text = text.Text.Replace(' ', PreservedSpace);
            }
        }

        List<string> lines = [];
        foreach (string raw in document.DocumentNode.InnerText.Split('\n'))
        {
            string line = WhitespaceRun.Replace(WebUtility.HtmlDecode(raw), " ").Trim();
            if (line.Length > 0)
            {
                lines.Add(line.Replace(PreservedSpace, ' '));
            }
        }

        return lines;
    }
}
