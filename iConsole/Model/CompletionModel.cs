using iConsole.Data;

namespace iConsole.Model;

public class CompletionModel : ICompletionModel
{
    private ConcurrentList<string> _completions { get; set; } = [];
    public IEnumerable<string> Completions => _completions;
    public ConcurrentObservableCollection<string> MatchingValues { get; internal set; } = [];
    public Task? UpdateTask { get; set; }

    public CompletionModel(IEnumerable<string> completions)
    {
        completions ??= Enumerable.Empty<string>();
        foreach (var completion in completions)
        {
            _completions.Add(completion);
        }
    }

    public void InsertCompletion(string value)
    {
        _completions.Add(value);
    }

    public void UpdateMatchingValues(string value)
    {
        MatchingValues.Clear();

        IEnumerable<string> matchingValues;
        if (string.IsNullOrEmpty(value))
        {
            matchingValues = Completions;
        }
        else
        {
            matchingValues = Completions.Where(c => c.StartsWith(value, StringComparison.InvariantCultureIgnoreCase));
        }

        foreach (var matchingValue in matchingValues)
        {
            MatchingValues.Add(matchingValue);
        }
    }
}
