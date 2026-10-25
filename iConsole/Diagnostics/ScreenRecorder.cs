using System.Globalization;
using System.Net;
using System.Text;
using iConsole.Buffers;
using iConsole.Display;

namespace iConsole.Diagnostics;

/// <summary>
/// Captures what is on screen and writes it out as plain text, HTML or SVG. Use it for bug reports, documentation
/// screenshots and for an end-to-end test to attach the exact screen when it fails.
///
/// <para><see cref="Capture"/> copies the merged frame, so later drawing does not change a recording. Wide characters
/// occupy the columns they occupy on the screen, in every format.</para>
/// </summary>
public sealed class ScreenRecorder
{
    private const int CellWidth = 9;
    private const int CellHeight = 18;
    private const int Baseline = 14;

    // The 16 colours of the terminal palette, in ConsoleColor order (the Color enum's values).
    private static readonly string[] Palette =
    [
        "#000000", "#000080", "#008000", "#008080", "#800000", "#800080", "#808000", "#C0C0C0",
        "#808080", "#0000FF", "#00FF00", "#00FFFF", "#FF0000", "#FF00FF", "#FFFF00", "#FFFFFF",
    ];

    private Conxel[][] _rows = [];

    /// <summary>Columns in the last capture.</summary>
    public int Width { get; private set; }

    /// <summary>Rows in the last capture.</summary>
    public int Height { get; private set; }

    /// <summary>Copies the merged frame of <paramref name="buffer"/>. For a live buffer, call <see cref="IBuffer.Merge"/> first.</summary>
    public void Capture(IBuffer buffer)
    {
        var layer = buffer.MergedLayer;
        Width = (int)buffer.Size.Width;
        Height = (int)buffer.Size.Height;
        _rows = new Conxel[Height][];
        for (var y = 0; y < Height; y++)
        {
            _rows[y] = layer[y].Span[..Math.Min(Width, layer[y].Length)].ToArray();
        }
    }

    /// <summary>The screen as text: one line per row, trailing spaces and trailing blank rows removed.</summary>
    public string ToText()
    {
        var lines = _rows.Select(row => TextWidth.FromCells(row.Select(cell => cell.Char)).TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>A self-contained HTML page showing the screen in a monospace block, with its colours and styles.</summary>
    public string ToHtml()
    {
        var builder = new StringBuilder();
        builder.Append("<!doctype html>\n<html><head><meta charset=\"utf-8\"><title>Screen</title>\n");
        builder.Append("<style>body{background:#000;margin:0;padding:8px}pre{font-family:monospace;line-height:1.2;margin:0}</style>\n");
        builder.Append("</head><body><pre>");

        for (var y = 0; y < _rows.Length; y++)
        {
            foreach (var run in Runs(_rows[y]))
            {
                builder.Append("<span style=\"");
                builder.Append(CssStyle(run.Style));
                builder.Append("\">");
                builder.Append(WebUtility.HtmlEncode(run.Text));
                builder.Append("</span>");
            }
            builder.Append('\n');
        }

        builder.Append("</pre></body></html>\n");
        return builder.ToString();
    }

    /// <summary>A self-contained SVG image of the screen: one background rectangle and one text element per run of cells.</summary>
    public string ToSvg()
    {
        var width = Width * CellWidth;
        var height = Height * CellHeight;
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">\n");
        builder.Append("<rect width=\"100%\" height=\"100%\" fill=\"#000000\"/>\n");

        for (var y = 0; y < _rows.Length; y++)
        {
            var column = 0;
            foreach (var run in Runs(_rows[y]))
            {
                var x = column * CellWidth;
                var runWidth = run.Columns * CellWidth;
                var top = y * CellHeight;
                builder.Append(CultureInfo.InvariantCulture,
                    $"<rect x=\"{x}\" y=\"{top}\" width=\"{runWidth}\" height=\"{CellHeight}\" fill=\"{Palette[(int)run.Style.Background]}\"/>\n");
                builder.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{x}\" y=\"{top + Baseline}\" textLength=\"{runWidth}\" lengthAdjust=\"spacingAndGlyphs\" xml:space=\"preserve\" font-family=\"monospace\" font-size=\"14\" fill=\"{Palette[(int)run.Style.Foreground]}\"{SvgFontStyle(run.Style)}>");
                builder.Append(WebUtility.HtmlEncode(run.Text));
                builder.Append("</text>\n");
                column += run.Columns;
            }
        }

        builder.Append("</svg>\n");
        return builder.ToString();
    }

    /// <summary>Writes the recording to <paramref name="path"/>, in the format its extension names (.txt, .html or .svg).</summary>
    public void Save(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var content = extension switch
        {
            ".txt" => ToText(),
            ".html" or ".htm" => ToHtml(),
            ".svg" => ToSvg(),
            _ => throw new ArgumentException($"Unknown recording format '{extension}'. Use .txt, .html or .svg.", nameof(path)),
        };
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private readonly record struct Style(Color Foreground, Color Background, bool IsBold, bool IsItalic, bool IsUnderline, bool IsStrikeThrough);

    private readonly record struct Run(string Text, int Columns, Style Style);

    /// <summary>Groups a row into runs of cells that share a style. Each run knows how many columns it covers.</summary>
    private static IEnumerable<Run> Runs(Conxel[] row)
    {
        var text = new StringBuilder();
        var columns = 0;
        Style? current = null;

        foreach (var cell in row)
        {
            var style = new Style(cell.ForegroundColor, cell.BackgroundColor, cell.IsBold, cell.IsItalic, cell.IsUnderline, cell.IsStrikeThrough);
            if (current is not null && style != current)
            {
                yield return new Run(TextWidth.FromCells(text.ToString()), columns, current.Value);
                text.Clear();
                columns = 0;
            }

            current = style;
            text.Append(cell.Char);
            columns++;
        }

        if (current is not null)
        {
            yield return new Run(TextWidth.FromCells(text.ToString()), columns, current.Value);
        }
    }

    private static string CssStyle(Style style)
    {
        var css = new StringBuilder();
        css.Append("color:").Append(Palette[(int)style.Foreground]).Append(';');
        css.Append("background:").Append(Palette[(int)style.Background]).Append(';');
        if (style.IsBold) css.Append("font-weight:bold;");
        if (style.IsItalic) css.Append("font-style:italic;");
        if (style.IsUnderline) css.Append("text-decoration:underline;");
        if (style.IsStrikeThrough) css.Append("text-decoration:line-through;");
        return css.ToString();
    }

    private static string SvgFontStyle(Style style)
    {
        var attributes = new StringBuilder();
        if (style.IsBold) attributes.Append(" font-weight=\"bold\"");
        if (style.IsItalic) attributes.Append(" font-style=\"italic\"");
        if (style.IsUnderline) attributes.Append(" text-decoration=\"underline\"");
        if (style.IsStrikeThrough) attributes.Append(" text-decoration=\"line-through\"");
        return attributes.ToString();
    }
}
