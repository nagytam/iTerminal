using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using iConsole.Helper;

namespace iConsole.Services;

/// <summary>
/// Reads the text of an EPUB e-book into lines, in reading order. An EPUB is a zip: <c>META-INF/container.xml</c>
/// names the package document (OPF), whose spine lists the chapters, each an XHTML file in the zip. Each chapter's
/// headings, paragraphs, list items and table cells become lines, and chapters follow one another with a blank line
/// between them. Images, styles, scripts and the navigation document are not shown.
/// </summary>
public static class EpubTextReader
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Elements that start and end a line of their own.</summary>
    private const string BlockElements = "//p|//div|//section|//article|//blockquote|//pre|//tr|//li|//dt|//dd|//figcaption|//table|//ul|//ol|//dl";

    public static IReadOnlyList<string> Read(byte[] content)
    {
        List<string> lines = [];
        try
        {
            using ZipArchive archive = new(new MemoryStream(content), ZipArchiveMode.Read);
            string? packagePath = PackagePath(archive);
            if (packagePath is null)
            {
                lines.Add("[Could not read the book: META-INF/container.xml names no package document]");
                return lines;
            }

            XDocument package = LoadXml(archive, packagePath);
            string directory = packagePath.Contains('/') ? packagePath[..(packagePath.LastIndexOf('/') + 1)] : string.Empty;

            Dictionary<string, string> manifest = package.Descendants()
                .Where(e => e.Name.LocalName == "item" && e.Attribute("id") is not null && e.Attribute("href") is not null)
                .ToDictionary(e => (string)e.Attribute("id")!, e => (string)e.Attribute("href")!);

            List<string> chapters = [.. package.Descendants()
                .Where(e => e.Name.LocalName == "itemref")
                .Select(e => (string?)e.Attribute("idref"))
                .Where(id => id is not null && manifest.ContainsKey(id))
                .Select(id => manifest[id!])];

            foreach (string href in chapters)
            {
                ZipArchiveEntry? entry = archive.GetEntry(directory + Uri.UnescapeDataString(href.Split('#')[0]));
                if (entry is null)
                {
                    continue;
                }

                List<string> chapter = ChapterLines(entry);
                if (chapter.Count == 0)
                {
                    continue;
                }

                if (lines.Count > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.AddRange(chapter);
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the book: {exception.Message}]");
        }

        return OfficeTextRenderer.Trim(lines);
    }

    private static string? PackagePath(ZipArchive archive)
    {
        ZipArchiveEntry? container = archive.GetEntry("META-INF/container.xml");
        if (container is null)
        {
            return null;
        }

        XDocument document = LoadXml(container);
        return document.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = archive.GetEntry(path)
            ?? throw new InvalidDataException($"{path} is missing");
        return LoadXml(entry);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>One chapter's text: its block elements as lines, lists with bullets, headings with hashes, table cells separated by bars.</summary>
    private static List<string> ChapterLines(ZipArchiveEntry entry)
    {
        byte[] bytes;
        using (Stream stream = entry.Open())
        using (MemoryStream copy = new())
        {
            stream.CopyTo(copy);
            bytes = copy.ToArray();
        }

        HtmlDocument document = new();
        document.LoadHtml(TextEncodings.Decode(bytes, out _));

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

        List<string> lines = [];
        foreach (string raw in document.DocumentNode.InnerText.Split('\n'))
        {
            string line = WhitespaceRun.Replace(WebUtility.HtmlDecode(raw), " ").Trim();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }
}
