using System.ComponentModel;

namespace iConsole.Model;

public interface IDirtyReadOnlyModel : INotifyPropertyChanged
{
    public bool IsDirty { get; }
}
