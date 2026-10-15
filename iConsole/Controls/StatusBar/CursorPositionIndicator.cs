using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
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
        var line =
            $"X: {_scrollableModel.CursorPosition.X}/{_scrollableModel.ScrollableSize.Width}" +
            $",Y: {_scrollableModel.CursorPosition.Y}/{_scrollableModel.ScrollableSize.Height}";
        var s = new ConxelString(line, Color.Green, Color.Blue);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
