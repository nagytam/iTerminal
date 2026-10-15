using iConsole.Model;
using iTerminal.Core.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iTerminal.Core.Model;

public class NetworkInfoModel : INotifyPropertyChanged
{
    public iConsole.Data.ConcurrentObservableCollection<NetworkInfo> Networks { get; private set; } = [];

    /// <summary>Set once <see cref="Jobs.NetworkInfoJob"/> has finished its first pass: until then an empty list means "not read yet", not "no interfaces".</summary>
    public volatile bool IsLoaded;

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}
