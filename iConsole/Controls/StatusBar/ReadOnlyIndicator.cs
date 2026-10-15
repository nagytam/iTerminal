using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.StatusBar;

public class ReadOnlyIndicator : StatusBarItem
{
    private IReadOnly _readOnly { get; set; }

    public ReadOnlyIndicator(
        IReadOnly readOnly,
        IThemeService themeService)
        : base(themeService)
    {
        _readOnly = readOnly;
        _readOnly.PropertyChanged += OnPropertyChanged;
        Name = nameof(ReadOnlyIndicator);
        ViewSize = new Size(10, 1);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var s = new ConxelString($"{(_readOnly.IsReadOnly ? "Read-Only" : "Writable")}"
        , Color.Green, Color.Blue);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
