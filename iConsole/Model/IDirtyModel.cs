using System.ComponentModel;

namespace iConsole.Model;

public interface IDirtyModel : INotifyPropertyChanged
{
    public bool IsDirty { get; set; }
}
