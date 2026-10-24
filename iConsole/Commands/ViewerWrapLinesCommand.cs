using iConsole.Controls.Viewers;
using iConsole.Data;

namespace iConsole.Commands;

/// <summary>
/// F2 in the viewer: wraps long lines to the width, or lets them run off to the right again. The cursor's line stays in
/// view across the change.
/// </summary>
public class ViewerWrapLinesCommand(TextViewerControl viewer) : Command
{
    public override string Title => "F2-Wrap";
    public override Key? Key { get; init; } = new Key(ConsoleKey.F2, false, false, false);

    /// <summary>Only in the text view: the hex dump has no lines to wrap.</summary>
    public override bool CanExecute() => base.CanExecute() && viewer.IsVisible;

    public override Task ExecuteAsync(object? arg)
    {
        long line = viewer.CursorLine - 1;
        long column = viewer.ScrollableModel.CursorPosition.X;
        viewer.WrapLines = !viewer.WrapLines;
        viewer.ShowLine(line, column);
        return Task.CompletedTask;
    }
}
