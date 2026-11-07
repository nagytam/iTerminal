using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iTerminal.Core.Controls;

/// <summary>
/// A block of read-only lines — "caption: value" rows, a status, a verdict — each in its own
/// colour. For dialogs whose content is facts rather than fields: a <see cref="Label"/> per row
/// would be a dozen controls to lay out, and none of them could be told to turn red.
///
/// <para>It paints every cell of its rectangle on every draw, so a value that got shorter, or a
/// row that went away, leaves nothing behind.</para>
/// </summary>
public class InfoLines(IThemeService themeService) : Control(themeService)
{
    /// <summary>One row: its text, and its colour — null for the theme's ordinary foreground.</summary>
    public readonly record struct Line(string Text, Color? Foreground = null);

    /// <summary>
    /// The lines with every one that is wider than <paramref name="width"/> broken at its spaces, the pieces keeping its colour and its indent.
    /// For a block of sentences: cut at the edge they stopped mid-word.
    /// </summary>
    public static List<Line> Wrap(IEnumerable<Line> lines, int width)
    {
        List<Line> wrapped = [];
        foreach (Line line in lines)
        {
            string text = line.Text ?? string.Empty;
            if (width < 8 || text.Length <= width)
            {
                wrapped.Add(line);
                continue;
            }

            int indent = text.Length - text.TrimStart(' ').Length;
            foreach (string piece in text.TrimStart(' ').WrapWords(width - indent))
            {
                wrapped.Add(new Line(new string(' ', indent) + piece, line.Foreground));
            }
        }

        return wrapped;
    }

    private IReadOnlyList<Line> _lines = [];

    public IReadOnlyList<Line> Lines
    {
        get => _lines;
        set
        {
            value ??= [];
            if (!_lines.SequenceEqual(value))
            {
                _lines = [.. value];
                StateHasChanged();
            }
        }
    }

    /// <summary>The rows as text, for a test to read.</summary>
    public string Text => string.Join(Environment.NewLine, _lines.Select(line => line.Text));

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var width = (int)ViewSize.Width;
        var height = (int)ViewSize.Height;
        if (width <= 0)
        {
            return;
        }

        var lines = _lines;
        for (var row = 0; row < height; row++)
        {
            var line = row < lines.Count ? lines[row] : default;
            var text = (line.Text ?? string.Empty).Cut(width).ExpandRight(width);
            buffer.Write(ZOrder,
                new Position(AbsolutePosition.X, AbsolutePosition.Y + row),
                new Size(width, 1),
                new ConxelString(text, line.Foreground ?? colorScheme.Foreground, colorScheme.Background));
        }
    }
}
