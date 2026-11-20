using iConsole.Data;
using iConsole.Helper;

namespace iConsole.Model;

public class CompletionModel : ICompletionModel
{
    /// <summary>Fewer typed letters than this match too much to be worth a fuzzy fallback (the command palette uses the same limit).</summary>
    private const int FuzzyMinimumLength = 3;

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
        // Build the whole result first and swap it in with one ReplaceAll, so a reader never sees the list empty
        // between the clear and the adds, and listeners get one change notification instead of one per value.
        IEnumerable<string> matchingValues;
        if (string.IsNullOrEmpty(value))
        {
            matchingValues = Completions;
        }
        else
        {
            matchingValues = Completions.Where(c => c.StartsWith(value, StringComparison.InvariantCultureIgnoreCase)).ToList();

            // A prefix that matches nothing usually means a skipped letter or a half-remembered name: fall back to a
            // ranked subsequence match, as the command palette does, so the list is not simply empty.
            if (!matchingValues.Any() && value.Length >= FuzzyMinimumLength)
            {
                matchingValues = Completions
                    .Select(c => (Completion: c, Match: FuzzyMatcher.Match(value, c)))
                    .Where(x => x.Match is not null)
                    .OrderByDescending(x => x.Match!.Value.Score)
                    .Select(x => x.Completion)
                    .ToList();
            }
        }

        MatchingValues.ReplaceAll(matchingValues.ToList());
    }
}
