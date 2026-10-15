using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iConsole.Data;

public class ObservableValue<T> :
    INotifyPropertyChanged where T : INotifyPropertyChanged
{
    private readonly T _propertyChangeNotifier;
    private readonly Func<T, string> _valueExtractor;
    public string Value => _valueExtractor(_propertyChangeNotifier);

    public ObservableValue(T propertyChangeNotifier, Func<T, string> valueExtractor)
    {
        _propertyChangeNotifier = propertyChangeNotifier;
        _valueExtractor = valueExtractor;
        _propertyChangeNotifier.PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Value));
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}
