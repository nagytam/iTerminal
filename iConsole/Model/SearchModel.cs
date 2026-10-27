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

    public bool UseRegex { get; set; }

    public bool MatchCase { get; set; } = true;

    private readonly Lock _lengthGate = new();
    private readonly Dictionary<(long X, long Y), int> _lengths = [];

    public int LengthAt(Position hit)
    {
        lock (_lengthGate)
        {
            return _lengths.TryGetValue((hit.X, hit.Y), out var length) ? length : Search.Length;
        }
    }

    public void SetHits(IEnumerable<iConsole.Helper.SearchHit> hits)
    {
        var list = hits as IReadOnlyList<iConsole.Helper.SearchHit> ?? [.. hits];
        lock (_lengthGate)
        {
            _lengths.Clear();
            foreach (var hit in list)
            {
                _lengths[(hit.Column, hit.Line)] = hit.Length;
            }
        }

        Hits.Clear();
        foreach (var hit in list)
        {
            Hits.Add(new Position(hit.Column, hit.Line));
        }
    }

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
