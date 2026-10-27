using iConsole.Data;
using System.ComponentModel;

namespace iConsole.Model;

public interface ISearchModel : INotifyPropertyChanged
{
    string Search { get; set; }
    ConcurrentObservableCollection<Position> Hits { get; }

    /// <summary>Whether <see cref="Search"/> is a regular expression.</summary>
    bool UseRegex { get => false; set { } }

    /// <summary>Whether upper and lower case must match.</summary>
    bool MatchCase { get => true; set { } }

    /// <summary>How many characters the hit at <paramref name="hit"/> covers: for plain text the length of <see cref="Search"/>, for a regular expression what it matched.</summary>
    int LengthAt(Position hit) => Search.Length;

    /// <summary>Replaces <see cref="Hits"/> with <paramref name="hits"/> and remembers how long each one is.</summary>
    void SetHits(IEnumerable<iConsole.Helper.SearchHit> hits)
    {
        Hits.Clear();
        foreach (var hit in hits)
        {
            Hits.Add(new Position(hit.Column, hit.Line));
        }
    }
}
