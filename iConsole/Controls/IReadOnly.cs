using System.ComponentModel;

namespace iConsole.Controls;

public interface IReadOnly : INotifyPropertyChanged
{
    bool IsReadOnly { get; set; }
}
