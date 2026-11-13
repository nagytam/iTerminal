using iConsole.Display;

namespace iConsole.Controls.Viewers;

/// <summary>The line-end mark and the edge column, drawn in the viewer and the editor.</summary>
public partial class TextViewerControl
{
    /// <summary>The character drawn after the last character of a line when whitespace is shown.</summary>
    public const char LineEndMark = '¶';

    private int _edgeColumn;

    /// <summary>
    /// The column (counted from 1) where a long line is marked, as Notepad++'s edge is; 0 for none. The cell in that column
    /// takes the dim colour, unless it is selected or highlighted, and the text itself is not changed.
    /// </summary>
    public int EdgeColumn
    {
        get => _edgeColumn;
        set
        {
            var column = Math.Max(0, value);
            if (_edgeColumn != column)
            {
                _edgeColumn = column;
                StateHasChanged();
            }
        }
    }

    /// <summary>
    /// Draws <see cref="LineEndMark"/> just after the last character of <paramref name="lineText"/> when whitespace is shown,
    /// in the dim colour; it is drawn only where the line ends inside the part <paramref name="drawn"/> shows.
    /// </summary>
    internal void MarkLineEnd(ConxelString drawn, string lineText, int startColumn, ColorScheme scheme)
    {
        if (!_showWhitespace)
        {
            return;
        }

        int x = lineText.Length - startColumn;
        if (x < 0 || x >= drawn.Count)
        {
            return;
        }

        Conxel cell = drawn.String[x];
        cell.Char = LineEndMark;
        if (cell.BackgroundColor == scheme.Background)
        {
            cell.ForegroundColor = scheme.DisabledForeground;
        }

        drawn.String[x] = cell;
    }

    /// <summary>Marks the edge column in the part of a line that <paramref name="drawn"/> shows, from <paramref name="startColumn"/> on.</summary>
    internal void MarkEdgeColumn(ConxelString drawn, int startColumn, ColorScheme scheme)
    {
        if (_edgeColumn <= 0)
        {
            return;
        }

        int x = _edgeColumn - 1 - startColumn;
        if (x < 0 || x >= drawn.Count)
        {
            return;
        }

        Conxel cell = drawn.String[x];
        if (cell.BackgroundColor == scheme.Background)
        {
            cell.BackgroundColor = scheme.DisabledForeground;
            drawn.String[x] = cell;
        }
    }
}
