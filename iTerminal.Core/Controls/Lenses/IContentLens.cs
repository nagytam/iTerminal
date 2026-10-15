using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;

namespace iTerminal.Core.Controls.Lenses;

public interface IContentLens
{
    string? GetHeaderIndicator(IContentModel contentModel);

    void DecorateRow(
        IContentModel contentModel,
        IEntry? entry,
        ConxelString row,
        bool cursor,
        ColorScheme colorScheme,
        VisualState visualState);
}
