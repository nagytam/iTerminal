using iConsole.Data;

namespace iConsole.Model;

public interface ICompletionModel
{
    Task UpdateTask { get; set; }
    ConcurrentObservableCollection<string> MatchingValues { get; }
    IEnumerable<string> Completions { get; }

    void InsertCompletion(string value);
    void UpdateMatchingValues(string value);
}
