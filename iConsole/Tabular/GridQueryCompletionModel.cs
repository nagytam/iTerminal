using iConsole.Controls;
using iConsole.Data;
using iConsole.Extensions;
using iConsole.Localization;
using iConsole.Model;

namespace iConsole.Tabular;

/// <summary>
/// Completion for the word being typed in a grid query line: the table's column names and the query's keywords. Every match is
/// the whole line with its last word completed, so the text box's ghost text and Tab, which both work on whole lines, complete a
/// word in the middle of a query. A word typed inside a quoted value, or after a number, is left alone.
/// </summary>
public sealed class GridQueryCompletionModel : ICompletionModel
{
    /// <summary>The words a query is built from, in the order a query uses them.</summary>
    public static readonly IReadOnlyList<string> Keywords =
    [
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "LIKE", "CONTAINS", "IS", "EMPTY", "ORDER", "BY", "ASC", "DESC", "LIMIT",
    ];

    /// <summary>
    /// How long a query line may be. The text box's own limit is 256 characters, which cuts a pasted query at an arbitrary
    /// point; a query that names a few dozen columns and conditions is far longer than that.
    /// </summary>
    public const int MaxQueryLength = 8192;

    private IReadOnlyList<string> _columns = [];

    /// <summary>Lifts the length limit of a query line and gives it word completion. Returns the model, to be given the table's columns.</summary>
    public static GridQueryCompletionModel Attach(TextBox queryBox)
    {
        var model = new GridQueryCompletionModel();
        queryBox.MaxLength = MaxQueryLength;
        queryBox.WithAutoCompletion(model);
        return model;
    }

    public Task? UpdateTask { get; set; }

    public ConcurrentObservableCollection<string> MatchingValues { get; } = [];

    /// <summary>The keywords and the table's columns: everything that can be completed.</summary>
    public IEnumerable<string> Completions => [.. _columns, .. Keywords];

    /// <summary>Sets the column names that are offered. Called when a table is shown; a column with a space or a comma is not offered, as it cannot be typed bare.</summary>
    public void SetColumns(IEnumerable<string> columns) =>
        _columns = [.. columns.Where(column => column.Length > 0 && column.All(c => !char.IsWhiteSpace(c) && c is not (',' or '"' or '\'')))];

    public void InsertCompletion(string value) => _columns = [.. _columns, value];

    public void UpdateMatchingValues(string value)
    {
        var matches = new List<string>();
        int start = value.Length;
        while (start > 0 && (char.IsLetterOrDigit(value[start - 1]) || value[start - 1] == '_'))
        {
            start--;
        }

        string word = value[start..];
        // Inside a quoted value (an odd number of quotes so far) or inside a number nothing is completed.
        bool inQuote = value.Count(c => c == '\'') % 2 == 1 || value.Count(c => c == '"') % 2 == 1;
        if (word.Length > 0 && !inQuote && !char.IsDigit(word[0]))
        {
            foreach (string candidate in Completions)
            {
                if (candidate.Length > word.Length && candidate.StartsWith(word, StringComparison.OrdinalIgnoreCase) && !matches.Contains(value[..start] + candidate))
                {
                    matches.Add(value[..start] + candidate);
                }
            }
        }

        MatchingValues.ReplaceAll(matches);
    }

    /// <summary>The key hints every query line shares: the operators and how to complete. Added to each viewer's own hints.</summary>
    public static IEnumerable<(string Key, string Description)> Hints()
    {
        yield return ("SELECT … WHERE", UiText.Get("Grid.Hint.Syntax", "Operators: = != < <= > >= LIKE, NOT LIKE, CONTAINS, IS [NOT] EMPTY; join with AND / OR; then ORDER BY column [DESC] and LIMIT n"));
        yield return ("Tab", UiText.Get("Grid.Hint.Complete", "Complete the word being typed: a column name or a keyword"));
    }
}
