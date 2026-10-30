using iConsole.Display;

namespace iConsole.Controls.Viewers;

/// <summary>Visible whitespace for the viewer and the editor.</summary>
public partial class TextViewerControl
{
    /// <summary>The character drawn for a space.</summary>
    public const char SpaceMark = '·';

    /// <summary>The character drawn for a tab.</summary>
    public const char TabMark = '→';

    private bool _showWhitespace;

    /// <summary>
    /// Draws spaces as <c>·</c> and tabs as <c>→</c> in the dim colour, and the spaces and tabs at the end of a line in the
    /// error colour, so a stray trailing space or a tab among spaces shows. The text itself is not changed.
    /// </summary>
    public bool ShowWhitespace
    {
        get => _showWhitespace;
        set
        {
            if (_showWhitespace != value)
            {
                _showWhitespace = value;
                StateHasChanged();
            }
        }
    }

    /// <summary>
    /// Marks the whitespace of the part of <paramref name="lineText"/> that <paramref name="drawn"/> shows: it starts at column
    /// <paramref name="startColumn"/> and covers at most <paramref name="count"/> characters. A selected or highlighted cell keeps
    /// its colours and only gets the mark.
    /// </summary>
    internal void MarkWhitespace(ConxelString drawn, string lineText, int startColumn, int count, ColorScheme scheme)
    {
        if (!_showWhitespace || startColumn >= lineText.Length)
        {
            return;
        }

        int shown = Math.Min(Math.Min(drawn.Count, count), lineText.Length - startColumn);
        int trailingFrom = lineText.TrimEnd(' ', '\t').Length;
        for (int x = 0; x < shown; x++)
        {
            char c = lineText[startColumn + x];
            if (c is not (' ' or '\t'))
            {
                continue;
            }

            Conxel cell = drawn.String[x];
            cell.Char = c == '\t' ? TabMark : SpaceMark;
            if (cell.BackgroundColor == scheme.Background)
            {
                cell.ForegroundColor = startColumn + x >= trailingFrom ? scheme.Error : scheme.DisabledForeground;
            }

            drawn.String[x] = cell;
        }
    }
}
