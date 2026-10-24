using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Services.Labels;

namespace iTerminal.Core.Controls.Lenses;

/// <summary>
/// Colours the name of a labelled file or folder in the file panel, in the colour it was given. The label is read from
/// memory (<see cref="IFileLabelService.Get"/>), so drawing a row never touches the database. The cursor row keeps its own
/// colours, as the git lens's does.
/// </summary>
public class FileLabelLens(IFileLabelService labels) : IContentLens
{
    public string? GetHeaderIndicator(IContentModel contentModel) => null;

    public void DecorateRow(
        IContentModel contentModel,
        IEntry? entry,
        ConxelString row,
        bool cursor,
        ColorScheme colorScheme,
        VisualState visualState)
    {
        _ = visualState;
        if (cursor || entry is null || FocusedEntry.IsParentEntry(entry) || entry.Url.ContainerType != ContainerType.FileSystem)
        {
            return;
        }

        var colour = labels.Get(entry.Url.Path).Colour;
        if (colour == LabelColour.None)
        {
            return;
        }

        var foreground = Foreground(colour);
        for (var i = 0; i < row.Count; i++)
        {
            var cell = row.String[i];
            cell.ForegroundColor = foreground;
            row.String[i] = cell;
        }
    }

    /// <summary>The terminal has no orange or purple: the nearest colours it has stand in for them.</summary>
    private static Color Foreground(LabelColour colour) => colour switch
    {
        LabelColour.Red => Color.Red,
        LabelColour.Orange => Color.DarkYellow,
        LabelColour.Yellow => Color.Yellow,
        LabelColour.Green => Color.Green,
        LabelColour.Blue => Color.Blue,
        LabelColour.Purple => Color.Magenta,
        _ => Color.Gray,
    };
}
