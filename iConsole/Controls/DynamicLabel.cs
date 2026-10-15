using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls;

public class DynamicLabel<T> 
    : Control where T : INotifyPropertyChanged
{
    public DynamicLabel(ObservableValue<T> observableValue, IThemeService themeService) 
        : base(themeService)
    {
        _observableValue = observableValue;
        _observableValue.PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StateHasChanged();
        }
    }

    private ObservableValue<T> _observableValue { get; }

    public ObservableValue<T> ObservableValue => _observableValue;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        var visualState = GetVisualState();
        GetColor(colorScheme, visualState, out Color foregroundColor, out Color backgroundColor);
        // Padded for the same reason as Label: the value changes under the control, and a shorter
        // one must erase the longer one it replaces.
        var text = (_observableValue.Value ?? string.Empty).PadRight((int)ViewSize.Width);
        var s = new ConxelString(text, foregroundColor, backgroundColor);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
