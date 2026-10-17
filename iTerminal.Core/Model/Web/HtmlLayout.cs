using System.Text;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Model.Web;

/// <summary>
/// Lays a <see cref="WebDocument"/> out as styled, word-wrapped text lines for a given width — the
/// text-mode counterpart of a rendering engine. Headings, paragraphs, lists, block quotes, preformatted
/// text, definition lists, tables (columns sized to their content), links, images (as their alt text) and
/// form controls are supported; scripts, styles and anything that is not text are left out. Layout of a
/// block is memoised for the duration of one <see cref="Layout"/> call, because tables measure their
/// cells before they place them.
/// </summary>
internal sealed partial class HtmlLayout(WebDocument document)
{
    private readonly WebDocument _document = document;

    private const int MaxLines = 200_000;
    private const int MeasureWidth = 400;

    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal)
    {
        "head", "script", "style", "template", "title", "meta", "link", "base", "svg", "canvas", "object", "embed",
        "datalist", "option", "optgroup", "audio", "source", "track", "param", "map", "math",
    };

    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "html", "body", "main", "article", "section", "nav", "aside", "header", "footer", "hgroup", "div", "center",
        "form", "dd", "summary", "legend", "li", "menu", "dir", "tbody", "thead", "tfoot", "tr", "td", "th", "caption",
        "noscript", "figcaption", "details", "dialog", "search",
    };

    private static readonly HashSet<string> Spaced = new(StringComparer.Ordinal)
    {
        "p", "blockquote", "pre", "fieldset", "figure", "address", "table", "dl",
    };

    private readonly record struct Key(HtmlNode Node, int Width, WebStyle Style, int Link, bool Pre);

    private readonly Dictionary<Key, List<WebLine>> _cache = [];
    private int _budget;

    [GeneratedRegex(@"(display\s*:\s*none|visibility\s*:\s*hidden)", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenStyleRegex();

    public WebPage Layout(int width)
    {
        width = Math.Max(10, width);
        _cache.Clear();
        _budget = MaxLines;

        var raw = Container(_document.Root, width, WebStyle.None, -1, false);

        List<WebLine> lines = new(raw.Count);
        foreach (var line in raw)
        {
            if (line.IsBlank && lines.Count > 0 && lines[^1].IsBlank && line.Anchors is null)
            {
                continue;
            }

            lines.Add(line);
        }

        _cache.Clear();
        return Finish(width, lines);
    }

    private WebPage Finish(int width, List<WebLine> lines)
    {
        List<WebItem> items = [];
        Dictionary<string, int> anchors = new(StringComparer.Ordinal);
        HashSet<int> seenLinks = [];
        HashSet<int> seenFields = [];

        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row];
            if (line.Anchors is not null)
            {
                foreach (var anchor in line.Anchors)
                {
                    anchors.TryAdd(anchor, row);
                }
            }

            var column = 0;
            foreach (var span in line.Spans)
            {
                if (span.Field >= 0)
                {
                    if (!_document.Fields[span.Field].Disabled && seenFields.Add(span.Field))
                    {
                        items.Add(new WebItem(true, span.Field, row, column));
                    }
                }
                else if (span.Link >= 0 && seenLinks.Add(span.Link))
                {
                    items.Add(new WebItem(false, span.Link, row, column));
                }

                column += span.Text.Length;
            }
        }

        return new WebPage { Width = width, Lines = lines, Items = items, Anchors = anchors };
    }

    private List<WebLine> Container(HtmlNode node, int width, WebStyle style, int link, bool pre)
    {
        width = Math.Max(1, width);
        var key = new Key(node, width, style, link, pre);
        if (_cache.TryGetValue(key, out var hit))
        {
            return hit;
        }

        var builder = new Builder(this, width, pre);
        if (node.Attr("id") is { Length: > 0 } id)
        {
            builder.AnchorHere(id);
        }

        foreach (var child in node.Children)
        {
            builder.Walk(child, style, link);
        }

        var lines = builder.Finish();
        _cache[key] = lines;
        return lines;
    }

    private bool IsHidden(HtmlNode node) =>
        Hidden.Contains(node.Tag)
        || node.HasAttr("hidden")
        || (node.Attr("style") is { Length: > 0 } css && HiddenStyleRegex().IsMatch(css));

    #region Builder

    /// <summary>Collects the children of one container: runs of inline content become wrapped lines, blocks are laid out and appended.</summary>
    private sealed class Builder(HtmlLayout owner, int width, bool pre)
    {
        private readonly List<WebLine> _output = [];
        private Flow? _flow;

        public void AnchorHere(string id) => (_flow ??= new Flow(owner, width)).Anchor(id);

        public List<WebLine> Finish()
        {
            Flush();
            var start = 0;
            var end = _output.Count;
            while (start < end && _output[start].IsBlank && _output[start].Anchors is null)
            {
                start++;
            }

            while (end > start && _output[end - 1].IsBlank && _output[end - 1].Anchors is null)
            {
                end--;
            }

            return _output.GetRange(start, end - start);
        }

        private void Flush()
        {
            if (_flow is not null)
            {
                _output.AddRange(_flow.Finish());
                _flow = null;
            }
        }

        private void Blank()
        {
            Flush();
            if (_output.Count > 0 && !_output[^1].IsBlank)
            {
                _output.Add(WebLine.Empty);
            }
        }

        private void AddBlock(List<WebLine> lines)
        {
            Flush();
            _output.AddRange(lines);
        }

        public void Walk(HtmlNode node, WebStyle style, int link)
        {
            if (owner._budget <= 0)
            {
                return;
            }

            if (node.IsText)
            {
                if (pre)
                {
                    (_flow ??= new Flow(owner, width)).Preformatted(node.Text, style | WebStyle.Code, link);
                }
                else
                {
                    (_flow ??= new Flow(owner, width)).Text(node.Text, style, link);
                }

                return;
            }

            if (owner.IsHidden(node))
            {
                return;
            }

            var tag = node.Tag;
            if (node.Attr("id") is { Length: > 0 } id)
            {
                AnchorHere(id);
            }

            if (tag == "a" && node.Attr("name") is { Length: > 0 } name)
            {
                AnchorHere(name);
            }

            switch (tag)
            {
                case "br":
                    (_flow ??= new Flow(owner, width)).Break();
                    return;
                case "hr":
                    Blank();
                    _output.Add(new WebLine([new WebSpan(new string('─', width), WebStyle.Dim)]));
                    Blank();
                    return;
                case "img":
                    Image(node, style, link);
                    return;
                case "input" or "button" or "select" or "textarea":
                    FieldControl(node, style, link);
                    return;
                case "iframe" or "frame":
                    Frame(node, style);
                    return;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Heading(node, style, link);
                    return;
                case "ul" or "ol":
                    List(node, style, link);
                    return;
                case "dl":
                    Blank();
                    AddBlock(owner.Container(node, width, style, link, false));
                    Blank();
                    return;
                case "dt":
                    Flush();
                    AddBlock(owner.Container(node, width, style | WebStyle.Bold, link, false));
                    return;
                case "dd":
                    Flush();
                    AddBlock(Indent(owner.Container(node, Math.Max(1, width - 4), style, link, false), "    ", "    ", WebStyle.None));
                    return;
                case "blockquote":
                    Blank();
                    AddBlock(Indent(owner.Container(node, Math.Max(1, width - 2), style | WebStyle.Quote, link, false), "│ ", "│ ", WebStyle.Dim));
                    Blank();
                    return;
                case "pre":
                    Blank();
                    AddBlock(owner.Container(node, width, style | WebStyle.Code, link, true));
                    Blank();
                    return;
                case "table":
                    Blank();
                    AddBlock(owner.Table(node, width, style, link));
                    Blank();
                    return;
                case "li":
                    AddBlock(Marked(node, style, link, "• ", 2));
                    return;
            }

            if (Containers.Contains(tag))
            {
                var margin = false;
                AddBlockContainer(node, style, link, margin);
                return;
            }

            if (Spaced.Contains(tag))
            {
                Blank();
                AddBlock(owner.Container(node, width, style, link, pre));
                Blank();
                return;
            }

            if (tag == "p")
            {
                Blank();
                AddBlock(owner.Container(node, width, style, link, pre));
                Blank();
                return;
            }

            // Everything else is inline: it styles its content and flows with the text around it.
            var childStyle = style;
            var childLink = link;
            switch (tag)
            {
                case "b" or "strong" or "th":
                    childStyle |= WebStyle.Bold;
                    break;
                case "i" or "em" or "cite" or "dfn" or "var" or "address":
                    childStyle |= WebStyle.Italic;
                    break;
                case "u" or "ins":
                    childStyle |= WebStyle.Underline;
                    break;
                case "s" or "strike" or "del":
                    childStyle |= WebStyle.Strike;
                    break;
                case "code" or "tt" or "kbd" or "samp":
                    childStyle |= WebStyle.Code;
                    break;
                case "mark":
                    childStyle |= WebStyle.Mark;
                    break;
                case "small":
                    childStyle |= WebStyle.Dim;
                    break;
                case "a" when owner._document.LinkOf.TryGetValue(node, out var linkId):
                    childStyle |= WebStyle.Link;
                    childLink = linkId;
                    break;
            }

            if (tag == "q")
            {
                (_flow ??= new Flow(owner, width)).Text("“", childStyle, childLink);
            }

            foreach (var child in node.Children)
            {
                Walk(child, childStyle, childLink);
            }

            if (tag == "q")
            {
                (_flow ??= new Flow(owner, width)).Text("”", childStyle, childLink);
            }
        }

        private void AddBlockContainer(HtmlNode node, WebStyle style, int link, bool margin)
        {
            if (margin)
            {
                Blank();
            }

            var childStyle = node.Tag == "th" ? style | WebStyle.Bold : style;
            AddBlock(owner.Container(node, width, childStyle, link, pre));
            if (margin)
            {
                Blank();
            }
        }

        private void Image(HtmlNode node, WebStyle style, int link)
        {
            var alt = WebDocument.Collapse(node.Attr("alt") ?? string.Empty);
            if (alt.Length == 0)
            {
                return;
            }

            (_flow ??= new Flow(owner, width)).Text("[" + alt + "]", style | WebStyle.Dim, link);
        }

        private void Frame(HtmlNode node, WebStyle style)
        {
            if (owner._document.LinkOf.TryGetValue(node, out var linkId))
            {
                var title = node.Attr("title") ?? node.Attr("src") ?? "frame";
                Blank();
                (_flow ??= new Flow(owner, width)).Text("[frame: " + WebDocument.Collapse(title) + "]", style | WebStyle.Link, linkId);
                Blank();
            }
        }

        private void FieldControl(HtmlNode node, WebStyle style, int link)
        {
            if (!owner._document.FieldOf.TryGetValue(node, out var fieldId))
            {
                return;
            }

            var field = owner._document.Fields[fieldId];
            if (field.Kind == WebFieldKind.Hidden)
            {
                return;
            }

            (_flow ??= new Flow(owner, width)).Field(fieldId, field.DisplayWidth(width), style, link);
        }

        private void Heading(HtmlNode node, WebStyle style, int link)
        {
            Blank();
            var lines = owner.Container(node, width, style | WebStyle.Bold | WebStyle.Heading, link, false);
            AddBlock(lines);
            if (node.Tag is "h1" or "h2" && lines.Count > 0)
            {
                var length = Math.Min(width, lines.Max(l => l.Length));
                _output.Add(new WebLine([new WebSpan(new string(node.Tag == "h1" ? '═' : '─', Math.Max(1, length)), WebStyle.Heading)]));
            }

            Blank();
        }

        private void List(HtmlNode node, WebStyle style, int link)
        {
            var depth = 0;
            for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent.Tag is "ul" or "ol")
                {
                    depth++;
                }
            }

            if (depth == 0)
            {
                Blank();
            }
            else
            {
                Flush();
            }

            var ordered = node.Tag == "ol";
            var number = int.TryParse(node.Attr("start"), out var start) ? start : 1;
            var items = node.Children.Count(c => c.Tag == "li");
            var digits = ordered ? (number + Math.Max(0, items - 1)).ToString().Length : 0;
            var bullet = depth switch { 0 => "• ", 1 => "◦ ", _ => "▪ " };
            if (node.Attr("id") is { Length: > 0 } id)
            {
                AnchorHere(id);
            }

            foreach (var child in node.Children)
            {
                if (child.Tag == "li")
                {
                    if (owner.IsHidden(child))
                    {
                        continue;
                    }

                    if (child.Attr("value") is { } value && int.TryParse(value, out var explicitNumber))
                    {
                        number = explicitNumber;
                    }

                    var marker = ordered ? (number.ToString() + ". ").PadLeft(digits + 2) : bullet;
                    AddBlock(Marked(child, style, link, marker, marker.Length));
                    number++;
                }
                else
                {
                    Walk(child, style, link);
                }
            }

            if (depth == 0)
            {
                Blank();
            }
        }

        private List<WebLine> Marked(HtmlNode item, WebStyle style, int link, string marker, int markerWidth)
        {
            var lines = owner.Container(item, Math.Max(1, width - markerWidth), style, link, false);
            if (lines.Count == 0)
            {
                lines = [WebLine.Empty];
            }

            return Indent(lines, marker, new string(' ', markerWidth), WebStyle.None);
        }
    }

    #endregion

    #region Flow

    /// <summary>Word-wraps inline content into lines of at most the given width.</summary>
    private sealed class Flow(HtmlLayout owner, int width)
    {
        private readonly List<WebLine> _lines = [];
        private readonly List<WebSpan> _current = [];
        private int _length;
        private bool _space;
        private List<string>? _anchors;

        public void Anchor(string id) => (_anchors ??= []).Add(id);

        public void Break() => EndLine(force: true);

        public void Text(string text, WebStyle style, int link)
        {
            var index = 0;
            while (index < text.Length)
            {
                if (IsBreakSpace(text[index]))
                {
                    _space = _length > 0;
                    index++;
                    continue;
                }

                var end = index;
                while (end < text.Length && !IsBreakSpace(text[end]))
                {
                    end++;
                }

                var word = Clean(text.AsSpan(index, end - index));
                index = end;
                if (word.Length > 0)
                {
                    Place(word, style, link);
                }
            }
        }

        public void Field(int fieldId, int fieldWidth, WebStyle style, int link)
        {
            var needed = fieldWidth + (_space && _length > 0 ? 1 : 0);
            if (_length > 0 && _length + needed > width)
            {
                EndLine();
            }

            if (_space && _length > 0)
            {
                AddRaw(" ", WebStyle.None, -1);
            }

            _space = false;
            var span = new WebSpan(new string(' ', Math.Min(fieldWidth, width)), style, -1, fieldId);
            _current.Add(span);
            _length += span.Text.Length;
        }

        /// <summary>Text that keeps its line breaks and spacing; lines longer than the width are cut into pieces.</summary>
        public void Preformatted(string text, WebStyle style, int link)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    EndLine(force: true);
                }

                var piece = Clean(lines[i].Replace("\t", "    ").AsSpan(), keepSpaces: true);
                while (piece.Length > 0)
                {
                    var room = width - _length;
                    if (room <= 0)
                    {
                        EndLine(force: true);
                        continue;
                    }

                    var take = Math.Min(room, piece.Length);
                    AddRaw(piece[..take], style, link);
                    piece = piece[take..];
                }
            }
        }

        public List<WebLine> Finish()
        {
            if (_length > 0 || _current.Count > 0 || (_anchors is not null && _lines.Count == 0))
            {
                EndLine(force: true);
            }

            return _lines;
        }

        private void Place(string word, WebStyle style, int link)
        {
            var needed = word.Length + (_space && _length > 0 ? 1 : 0);
            if (_length > 0 && _length + needed > width)
            {
                EndLine();
            }

            if (_space && _length > 0)
            {
                var sameLink = _current.Count > 0 && _current[^1].Link == link && _current[^1].Field < 0;
                AddRaw(" ", sameLink ? style : WebStyle.None, sameLink ? link : -1);
            }

            _space = false;
            while (word.Length > width - _length)
            {
                var room = width - _length;
                if (room <= 0)
                {
                    EndLine(force: true);
                    continue;
                }

                AddRaw(word[..room], style, link);
                word = word[room..];
                EndLine(force: true);
            }

            AddRaw(word, style, link);
        }

        private void AddRaw(string text, WebStyle style, int link)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (_current.Count > 0)
            {
                var last = _current[^1];
                if (last.Field < 0 && last.Style == style && last.Link == link)
                {
                    _current[^1] = last with { Text = last.Text + text };
                    _length += text.Length;
                    return;
                }
            }

            _current.Add(new WebSpan(text, style, link));
            _length += text.Length;
        }

        private void EndLine(bool force = false)
        {
            _space = false;
            if (_current.Count == 0 && !force)
            {
                return;
            }

            if (owner._budget-- <= 0)
            {
                _current.Clear();
                _length = 0;
                return;
            }

            _lines.Add(new WebLine(_current.ToArray(), _anchors?.ToArray()));
            _anchors = null;
            _current.Clear();
            _length = 0;
        }

        private static bool IsBreakSpace(char c) => char.IsWhiteSpace(c) && c != '\u00A0' && c != '\u202F';
    }

    private static string Clean(ReadOnlySpan<char> text, bool keepSpaces = false)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\u00A0' or '\u202F')
            {
                builder.Append(' ');
            }
            else if (c == ' ' && keepSpaces)
            {
                builder.Append(' ');
            }
            else if (c < ' ' || (c >= '\u007F' && c <= '\u009F') || (c >= '\u200B' && c <= '\u200F') || c is '\u2060' or '\uFEFF' or '\u00AD' or '\u2028' or '\u2029')
            {
                continue;
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    #endregion

    #region Blocks

    /// <summary>Puts <paramref name="first"/> before the first line and <paramref name="rest"/> before the others.</summary>
    private static List<WebLine> Indent(List<WebLine> lines, string first, string rest, WebStyle prefixStyle)
    {
        List<WebLine> result = new(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var prefix = i == 0 ? first : rest;
            var line = lines[i];
            if (prefix.Trim().Length == 0 && line.IsBlank && line.Anchors is null)
            {
                result.Add(line);
                continue;
            }

            var spans = new WebSpan[line.Spans.Length + 1];
            spans[0] = new WebSpan(prefix, prefixStyle);
            Array.Copy(line.Spans, 0, spans, 1, line.Spans.Length);
            result.Add(new WebLine(spans, line.Anchors));
        }

        return result;
    }

    private sealed record Cell(HtmlNode Node, int Span, bool Header);

    private List<WebLine> Table(HtmlNode table, int width, WebStyle style, int link)
    {
        List<List<Cell>> rows = [];
        HtmlNode? caption = null;
        CollectRows(table, rows, ref caption);

        List<WebLine> output = [];
        if (caption is not null)
        {
            output.AddRange(Container(caption, width, style | WebStyle.Bold, link, false));
            output.Add(WebLine.Empty);
        }

        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Sum(c => c.Span));
        if (columns == 0)
        {
            return output;
        }

        var bordered = int.TryParse(table.Attr("border"), out var border) && border > 0;
        var separator = bordered ? " │ " : "  ";
        var natural = new int[columns];
        var minimum = new int[columns];

        foreach (var row in rows)
        {
            var column = 0;
            foreach (var cell in row)
            {
                if (cell.Span == 1 && column < columns)
                {
                    var lines = Container(cell.Node, MeasureWidth, CellStyle(cell, style), link, false);
                    natural[column] = Math.Max(natural[column], lines.Count == 0 ? 0 : Math.Min(MeasureWidth, lines.Max(l => l.Length)));
                    minimum[column] = Math.Max(minimum[column], Math.Min(LongestWord(cell.Node), natural[column]));
                }

                column += cell.Span;
            }
        }

        // A cell that spans columns needs room too: whatever the single cells did not give it is added to its first column.
        foreach (var row in rows)
        {
            var column = 0;
            foreach (var cell in row)
            {
                if (cell.Span > 1)
                {
                    var lines = Container(cell.Node, MeasureWidth, CellStyle(cell, style), link, false);
                    var need = lines.Count == 0 ? 0 : Math.Min(MeasureWidth, lines.Max(l => l.Length));
                    var have = 0;
                    var active = 0;
                    for (var i = column; i < Math.Min(columns, column + cell.Span); i++)
                    {
                        have += natural[i];
                        if (natural[i] > 0)
                        {
                            active++;
                        }
                    }

                    have += Math.Max(0, active - 1) * separator.Length;
                    if (need > have && column < columns)
                    {
                        natural[column] += need - have;
                        minimum[column] = Math.Max(minimum[column], Math.Min(natural[column], 1));
                    }
                }

                column += cell.Span;
            }
        }

        var activeColumns = Enumerable.Range(0, columns).Where(c => natural[c] > 0).ToList();
        if (activeColumns.Count == 0)
        {
            return output;
        }

        var available = width - separator.Length * (activeColumns.Count - 1);
        if (activeColumns.Count == 1 || available < activeColumns.Count * 3 || columns > 40)
        {
            // Not enough room for side-by-side columns (or only one has any content): stack the cells.
            foreach (var row in rows)
            {
                foreach (var cell in row)
                {
                    var lines = Container(cell.Node, width, CellStyle(cell, style), link, false);
                    if (lines.Count > 0)
                    {
                        output.AddRange(lines);
                        if (bordered || (rows.Count > 1 && activeColumns.Count > 1))
                        {
                            output.Add(WebLine.Empty);
                        }
                    }
                }
            }

            return output;
        }

        var widths = new int[columns];
        var totalNatural = activeColumns.Sum(c => natural[c]);
        if (totalNatural <= available)
        {
            foreach (var c in activeColumns)
            {
                widths[c] = natural[c];
            }
        }
        else
        {
            var totalMinimum = activeColumns.Sum(c => Math.Max(1, minimum[c]));
            if (totalMinimum >= available)
            {
                var assigned = 0;
                foreach (var c in activeColumns)
                {
                    widths[c] = Math.Max(3, (int)((long)natural[c] * available / totalNatural));
                    assigned += widths[c];
                }

                // Rounding and the floor of 3 can overshoot; take it back from the widest column.
                while (assigned > available)
                {
                    var widest = activeColumns.MaxBy(c => widths[c]);
                    if (widths[widest] <= 3)
                    {
                        break;
                    }

                    widths[widest]--;
                    assigned--;
                }
            }
            else
            {
                var extra = available - totalMinimum;
                var totalWeight = activeColumns.Sum(c => Math.Max(0, natural[c] - Math.Max(1, minimum[c])));
                var given = 0;
                foreach (var c in activeColumns)
                {
                    var floor = Math.Max(1, minimum[c]);
                    var share = totalWeight == 0 ? 0 : (int)((long)extra * Math.Max(0, natural[c] - floor) / totalWeight);
                    widths[c] = floor + share;
                    given += share;
                }

                for (var i = 0; given < extra && i < activeColumns.Count * 2; i++)
                {
                    var c = activeColumns[i % activeColumns.Count];
                    if (widths[c] < natural[c])
                    {
                        widths[c]++;
                        given++;
                    }
                }
            }
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var composed = ComposeRow(rows[r], columns, widths, activeColumns, separator, style, link, bordered);
            if (composed.Count == 0)
            {
                continue;
            }

            if (bordered && output.Count > 0 && r > 0)
            {
                output.Add(Rule(widths, activeColumns, width));
            }

            output.AddRange(composed);
        }

        return output;
    }

    private static WebStyle CellStyle(Cell cell, WebStyle style) => cell.Header ? style | WebStyle.Bold : style;

    private static WebLine Rule(int[] widths, List<int> active, int limit)
    {
        var text = string.Join("─┼─", active.Select(c => new string('─', widths[c])));
        return new WebLine([new WebSpan(text.Length > limit ? text[..limit] : text, WebStyle.Dim)]);
    }

    private List<WebLine> ComposeRow(List<Cell> row, int columns, int[] widths, List<int> active, string separator, WebStyle style, int link, bool bordered)
    {
        // One block of lines per cell, with the width of the columns it covers.
        List<(List<WebLine> Lines, int Width)> blocks = [];
        var column = 0;
        foreach (var cell in row)
        {
            var covered = Enumerable.Range(column, Math.Max(0, Math.Min(columns, column + cell.Span) - column)).Where(active.Contains).ToList();
            column += cell.Span;
            if (covered.Count == 0)
            {
                continue;
            }

            var cellWidth = covered.Sum(c => widths[c]) + separator.Length * (covered.Count - 1);
            blocks.Add((Container(cell.Node, cellWidth, CellStyle(cell, style), link, false), cellWidth));
        }

        var height = blocks.Count == 0 ? 0 : blocks.Max(b => b.Lines.Count);
        if (height == 0)
        {
            return [];
        }

        List<WebLine> result = new(height);
        for (var y = 0; y < height; y++)
        {
            List<WebSpan> spans = [];
            string[]? anchors = null;
            for (var b = 0; b < blocks.Count; b++)
            {
                var (lines, cellWidth) = blocks[b];
                var length = 0;
                if (y < lines.Count)
                {
                    spans.AddRange(lines[y].Spans);
                    length = lines[y].Length;
                    anchors = lines[y].Anchors is { } a ? anchors is null ? a : [.. anchors, .. a] : anchors;
                }

                if (b < blocks.Count - 1)
                {
                    spans.Add(new WebSpan(new string(' ', Math.Max(0, cellWidth - length)), WebStyle.None));
                    spans.Add(new WebSpan(separator, WebStyle.Dim));
                }
            }

            result.Add(new WebLine(spans.ToArray(), anchors));
        }

        return result;
    }

    private static void CollectRows(HtmlNode node, List<List<Cell>> rows, ref HtmlNode? caption)
    {
        foreach (var child in node.Children)
        {
            switch (child.Tag)
            {
                case "tr":
                    List<Cell> cells = [];
                    foreach (var cell in child.Children)
                    {
                        if (cell.Tag is "td" or "th")
                        {
                            var span = int.TryParse(cell.Attr("colspan"), out var parsed) ? Math.Clamp(parsed, 1, 40) : 1;
                            cells.Add(new Cell(cell, span, cell.Tag == "th"));
                        }
                    }

                    rows.Add(cells);
                    break;
                case "thead" or "tbody" or "tfoot":
                    CollectRows(child, rows, ref caption);
                    break;
                case "caption":
                    caption ??= child;
                    break;
            }
        }
    }

    private static int LongestWord(HtmlNode node)
    {
        var longest = 1;
        foreach (var word in node.InnerText().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            longest = Math.Max(longest, word.Length);
        }

        return Math.Min(longest, 40);
    }

    #endregion
}
