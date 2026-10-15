using iConsole.Data;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iConsole.Model;

public class SearchModel : ISearchModel
{
    private string _search = string.Empty;
    public string Search
    {
        get
        {
            return _search;
        }
        set
        {
            if (_search != value)
            {
                _search = value;
                OnPropertyChanged(nameof(Search));
            }
        }
    }

    public ConcurrentObservableCollection<Position> Hits { get; private set; } = [];

    public SearchModel()
    {
        Hits.CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Hits));
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}
