using System.Net;
using System.Text;

namespace iTerminal.Core.Model.Web;

/// <summary>One element, text run or the root of a parsed HTML document.</summary>
internal sealed class HtmlNode(string tag)
{
    public const string TextTag = "#text";
    public const string RootTag = "#root";

    private static readonly Dictionary<string, string> NoAttributes = new(0);

    /// <summary>Lower-case tag name, <see cref="TextTag"/> for a text run or <see cref="RootTag"/>.</summary>
    public string Tag { get; } = tag;

    /// <summary>The (entity-decoded) text of a text node.</summary>
    public string Text { get; set; } = string.Empty;

    public Dictionary<string, string> Attributes { get; set; } = NoAttributes;

    public HtmlNode? Parent { get; set; }

    public List<HtmlNode> Children { get; } = [];

    public bool IsText => Tag == TextTag;

    public string? Attr(string name) => Attributes.TryGetValue(name, out var value) ? value : null;

    public bool HasAttr(string name) => Attributes.ContainsKey(name);

    /// <summary>All the text below this node, in document order.</summary>
    public string InnerText()
    {
        var builder = new StringBuilder();
        Collect(this, builder);
        return builder.ToString();
    }

    private static void Collect(HtmlNode node, StringBuilder builder)
    {
        if (node.IsText)
        {
            builder.Append(node.Text);
            return;
        }

        foreach (var child in node.Children)
        {
            Collect(child, builder);
        }
    }
}

/// <summary>
/// A forgiving HTML parser: it never throws, closes what the page forgot to close, and keeps
/// <c>script</c>/<c>style</c> out of the tree. It builds just enough of a DOM for the text layout —
/// it is not the HTML5 tree-construction algorithm and does not try to repair mis-nested markup the
/// way a graphical browser does.
/// </summary>
internal static class HtmlParser
{
    /// <summary>Nesting deeper than this is flattened, which keeps the recursive layout within the stack.</summary>
    private const int MaxDepth = 200;

    private static readonly HashSet<string> VoidTags = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
        "frame", "command", "keygen",
    };

    private static readonly HashSet<string> RawTextTags = new(StringComparer.Ordinal) { "script", "style" };

    private static readonly HashSet<string> EscapableRawTextTags = new(StringComparer.Ordinal) { "textarea", "title" };

    /// <summary>Opening one of these ends an open paragraph.</summary>
    private static readonly HashSet<string> ClosesParagraph = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl", "fieldset",
        "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "main",
        "menu", "nav", "ol", "p", "pre", "section", "table", "ul", "li", "dd", "dt",
    };

    private static readonly string[] ParagraphBoundary = ["td", "th", "li", "dd", "dt", "caption", "button", "select", "body", "html", "blockquote"];

    public static HtmlNode Parse(string html)
    {
        var root = new HtmlNode(HtmlNode.RootTag);
        var stack = new List<HtmlNode> { root };
        var position = 0;
        var length = html.Length;

        while (position < length)
        {
            var lt = html.IndexOf('<', position);
            if (lt < 0)
            {
                AddText(stack, html[position..]);
                break;
            }

            if (lt > position)
            {
                AddText(stack, html[position..lt]);
            }

            position = lt;
            if (StartsWith(html, position, "<!--"))
            {
                var end = html.IndexOf("-->", position + 4, StringComparison.Ordinal);
                position = end < 0 ? length : end + 3;
                continue;
            }

            if (position + 1 < length && (html[position + 1] == '!' || html[position + 1] == '?'))
            {
                var end = html.IndexOf('>', position);
                position = end < 0 ? length : end + 1;
                continue;
            }

            if (position + 1 < length && html[position + 1] == '/')
            {
                position = ParseEndTag(html, position, stack);
                continue;
            }

            if (position + 1 < length && char.IsAsciiLetter(html[position + 1]))
            {
                position = ParseStartTag(html, position, stack);
                continue;
            }

            // A "<" that starts nothing is text.
            AddText(stack, "<");
            position++;
        }

        return root;
    }

    private static int ParseEndTag(string html, int position, List<HtmlNode> stack)
    {
        var end = html.IndexOf('>', position);
        var close = end < 0 ? html.Length : end;
        var name = html[(position + 2)..close].Trim().ToLowerInvariant();
        var space = name.IndexOfAny([' ', '\t', '\r', '\n', '/']);
        if (space >= 0)
        {
            name = name[..space];
        }

        for (var i = stack.Count - 1; i > 0; i--)
        {
            if (stack[i].Tag == name)
            {
                stack.RemoveRange(i, stack.Count - i);
                break;
            }
        }

        return end < 0 ? html.Length : end + 1;
    }

    private static int ParseStartTag(string html, int position, List<HtmlNode> stack)
    {
        var index = position + 1;
        var nameStart = index;
        while (index < html.Length && !char.IsWhiteSpace(html[index]) && html[index] != '>' && html[index] != '/')
        {
            index++;
        }

        var name = html[nameStart..index].ToLowerInvariant();
        Dictionary<string, string>? attributes = null;
        var selfClosing = false;

        while (index < html.Length)
        {
            while (index < html.Length && char.IsWhiteSpace(html[index]))
            {
                index++;
            }

            if (index >= html.Length)
            {
                break;
            }

            if (html[index] == '>')
            {
                index++;
                break;
            }

            if (html[index] == '/')
            {
                selfClosing = true;
                index++;
                continue;
            }

            selfClosing = false;
            var attributeStart = index;
            while (index < html.Length && !char.IsWhiteSpace(html[index]) && html[index] != '=' && html[index] != '>' && html[index] != '/')
            {
                index++;
            }

            if (index == attributeStart)
            {
                index++;
                continue;
            }

            var attributeName = html[attributeStart..index].ToLowerInvariant();
            while (index < html.Length && char.IsWhiteSpace(html[index]))
            {
                index++;
            }

            var value = string.Empty;
            if (index < html.Length && html[index] == '=')
            {
                index++;
                while (index < html.Length && char.IsWhiteSpace(html[index]))
                {
                    index++;
                }

                if (index < html.Length && (html[index] == '"' || html[index] == '\''))
                {
                    var quote = html[index];
                    var valueEnd = html.IndexOf(quote, index + 1);
                    if (valueEnd < 0)
                    {
                        valueEnd = html.Length;
                    }

                    value = html[(index + 1)..valueEnd];
                    index = Math.Min(html.Length, valueEnd + 1);
                }
                else
                {
                    var valueStart = index;
                    while (index < html.Length && !char.IsWhiteSpace(html[index]) && html[index] != '>')
                    {
                        index++;
                    }

                    value = html[valueStart..index];
                }
            }

            attributes ??= new Dictionary<string, string>(StringComparer.Ordinal);
            attributes.TryAdd(attributeName, WebUtility.HtmlDecode(value));
        }

        CloseImplied(stack, name);

        var node = new HtmlNode(name) { Parent = stack[^1] };
        if (attributes is not null)
        {
            node.Attributes = attributes;
        }

        stack[^1].Children.Add(node);

        if (RawTextTags.Contains(name) || EscapableRawTextTags.Contains(name))
        {
            var closing = IndexOfIgnoreCase(html, "</" + name, index);
            var contentEnd = closing < 0 ? html.Length : closing;
            if (EscapableRawTextTags.Contains(name) && contentEnd > index)
            {
                var text = new HtmlNode(HtmlNode.TextTag) { Text = WebUtility.HtmlDecode(html[index..contentEnd]), Parent = node };
                node.Children.Add(text);
            }

            if (closing < 0)
            {
                return html.Length;
            }

            var gt = html.IndexOf('>', closing);
            return gt < 0 ? html.Length : gt + 1;
        }

        if (!selfClosing && !VoidTags.Contains(name) && stack.Count < MaxDepth)
        {
            stack.Add(node);
        }

        return index;
    }

    private static void AddText(List<HtmlNode> stack, string raw)
    {
        if (raw.Length == 0)
        {
            return;
        }

        var parent = stack[^1];
        var text = WebUtility.HtmlDecode(raw);
        if (parent.Children.Count > 0 && parent.Children[^1].IsText)
        {
            parent.Children[^1].Text += text;
            return;
        }

        parent.Children.Add(new HtmlNode(HtmlNode.TextTag) { Text = text, Parent = parent });
    }

    /// <summary>Pops what the incoming tag ends implicitly: an open paragraph, list item, table cell or option.</summary>
    private static void CloseImplied(List<HtmlNode> stack, string incoming)
    {
        string[] closes;
        string[] boundary;
        switch (incoming)
        {
            case "li":
                closes = ["li"];
                boundary = ["ul", "ol", "menu", "dir", "table"];
                break;
            case "dt":
            case "dd":
                closes = ["dt", "dd"];
                boundary = ["dl", "table"];
                break;
            case "tr":
                closes = ["tr"];
                boundary = ["table"];
                break;
            case "td":
            case "th":
                closes = ["td", "th"];
                boundary = ["tr", "table"];
                break;
            case "thead":
            case "tbody":
            case "tfoot":
                closes = ["thead", "tbody", "tfoot"];
                boundary = ["table"];
                break;
            case "option":
                closes = ["option"];
                boundary = ["select", "optgroup", "datalist"];
                break;
            case "optgroup":
                closes = ["option", "optgroup"];
                boundary = ["select"];
                break;
            default:
                if (!ClosesParagraph.Contains(incoming))
                {
                    return;
                }

                closes = ["p"];
                boundary = ParagraphBoundary;
                break;
        }

        for (var i = stack.Count - 1; i > 0; i--)
        {
            var tag = stack[i].Tag;
            if (Array.IndexOf(closes, tag) >= 0)
            {
                stack.RemoveRange(i, stack.Count - i);
                return;
            }

            if (Array.IndexOf(boundary, tag) >= 0)
            {
                return;
            }
        }
    }

    private static bool StartsWith(string text, int position, string prefix) =>
        string.CompareOrdinal(text, position, prefix, 0, prefix.Length) == 0;

    private static int IndexOfIgnoreCase(string text, string value, int start) =>
        start >= text.Length ? -1 : text.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
}
