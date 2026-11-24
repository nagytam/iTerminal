using iConsole.Data;
using iConsole.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Model;

public class PersistedValuesCompletionModel : ICompletionModel
{
    private ISettingsService _settingsService { get; set; }
    private readonly string _key;
    private ConcurrentList<string> _completions { get; set; } = [];
    public IEnumerable<string> Completions => _completions;
    public ConcurrentObservableCollection<string> MatchingValues { get; internal set; } = [];
    public Task? UpdateTask { get; set; }

    public PersistedValuesCompletionModel(ISettingsService settingsService, string key)
    {
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _key = key;
        var values = _settingsService.Get(_key);
        if (!string.IsNullOrEmpty(values))
        {
            // A blank entry (a trailing ';', or one saved before blanks were refused) is not a completion.
            foreach (var value in values.Split(';').Where(value => !string.IsNullOrWhiteSpace(value) && !_completions.Contains(value)))
            {
                _completions.Add(value);
            }
        }
    }

    /// <summary>
    /// Remembers a value for the completions. A blank value is not remembered, and neither is one with a ';': the list is
    /// stored separated by ';', so such a value would read back as two entries and corrupt the history.
    /// </summary>
    public void InsertCompletion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(';'))
        {
            return;
        }

        if (!Completions.Contains(value))
        {
            _completions.Add(value);
        }
        var values = string.Join(';', Completions);
        _settingsService.Set(_key, values);
    }

    public void UpdateMatchingValues(string value)
    {
        MatchingValues.Clear();
        // Entries that start with the text go first (the sort is stable, so each group keeps its order): the box greys the
        // first match after the cursor, and only a prefix match can extend what was typed.
        var matchingValues = Completions
            .Where(c => c.Contains(value, StringComparison.InvariantCultureIgnoreCase))
            .OrderBy(c => c.StartsWith(value, StringComparison.InvariantCultureIgnoreCase) ? 0 : 1);
        foreach (var matchingValue in matchingValues)
        {
            MatchingValues.Add(matchingValue);
        }
    }
}
