using iConsole.Data;
using System.ComponentModel;

namespace iConsole.Model;

public interface ISearchModel : INotifyPropertyChanged
{
    string Search { get; set; }
    ConcurrentObservableCollection<Position> Hits { get; }
}
