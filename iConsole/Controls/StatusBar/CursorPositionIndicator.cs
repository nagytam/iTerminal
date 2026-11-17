using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.StatusBar;

public class CursorPositionIndicator : StatusBarItem
{
    private IScrollableModel _scrollableModel { get; set; }

    public CursorPositionIndicator(
        IScrollableModel scrollableModel,
        IThemeService themeService)
        : base(themeService)
    {
        _scrollableModel = scrollableModel;
        _scrollableModel.PropertyChanged += OnPropertyChanged;
        Name = nameof(CursorPosition);
        ViewSize = new Size(20, 1);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        // Counted from 1, as people count lines and columns; the totals are the document's size. "X:" and "Y:" were
        // not translated and did not say which was the line.
        var line = string.Format(
            UiText.Get("Status.LineColumn", "Ln {0}/{1} Col {2}/{3}"),
            _scrollableModel.CursorPosition.Y + 1,
            _scrollableModel.ScrollableSize.Height,
            _scrollableModel.CursorPosition.X + 1,
            _scrollableModel.ScrollableSize.Width);
        var s = new ConxelString(line, Color.Green, Color.Blue);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
