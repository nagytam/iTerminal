using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace iConsole.Services;

/// <summary>
/// Reads a FictionBook 2 e-book (.fb2) into lines: each body and section as headings and paragraphs, poem verses and
/// table cells on lines of their own. The description, the cover and the other embedded binaries are not shown.
/// </summary>
public static class Fb2TextReader
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static IReadOnlyList<string> Read(byte[] content)
    {
        List<string> lines = [];
        try
        {
            XDocument document = XDocument.Load(new MemoryStream(content));
            if (document.Root?.Name.LocalName != "FictionBook")
            {
                return ["[Could not read the book: the file is not a FictionBook]"];
            }

            foreach (XElement body in document.Root.Elements().Where(e => e.Name.LocalName == "body"))
            {
                if (body.Attribute("name") is { } name)
                {
                    OfficeTextRenderer.AppendHeading(lines, name.Value);
                }

                Blocks(lines, body, 1);
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the book: {exception.Message}]");
        }

        return OfficeTextRenderer.Trim(lines);
    }

    /// <summary>The block elements of a body, epigraph, section, poem or stanza, in order.</summary>
    private static void Blocks(List<string> lines, XElement container, int depth)
    {
        foreach (XElement child in container.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "title":
                    lines.Add(new string('#', Math.Min(depth, 6)) + " " + Text(child));
                    lines.Add(string.Empty);
                    break;

                case "subtitle":
                    lines.Add(new string('#', Math.Min(depth + 1, 6)) + " " + Text(child));
                    break;

                case "p":
                    lines.Add(Text(child));
                    break;

                case "v":
                    lines.Add("  " + Text(child));
                    break;

                case "text-author":
                    lines.Add("— " + Text(child));
                    break;

                case "empty-line":
                    lines.Add(string.Empty);
                    break;

                case "table":
                    List<IReadOnlyList<string>> rows = [.. child.Elements().Where(e => e.Name.LocalName == "tr")
                        .Select(row => (IReadOnlyList<string>)row.Elements().Select(Text).ToList())];
                    OfficeTextRenderer.AppendTextTable(lines, rows);
                    lines.Add(string.Empty);
                    break;

                case "section":
                    Blocks(lines, child, depth + 1);
                    break;

                case "epigraph":
                case "annotation":
                case "cite":
                case "poem":
                case "stanza":
                case "history":
                    Blocks(lines, child, depth);
                    lines.Add(string.Empty);
                    break;
            }
        }
    }

    /// <summary>An element's text, with its inline markup (emphasis, links, notes) and its whitespace flattened.</summary>
    private static string Text(XElement element) => Spaces.Replace(element.Value, " ").Trim();
}
