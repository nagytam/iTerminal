namespace iConsole.Prompts;

/// <summary>What a choice prompt asks, and the choices it offers. Give the prompt to <see cref="Services.IPromptService"/>.</summary>
public abstract class ChoicePrompt<T>(string question, IEnumerable<T> choices)
{
    /// <summary>The question shown above the list.</summary>
    public string Question { get; } = question;

    /// <summary>The choices, in the order they are shown.</summary>
    public IReadOnlyList<T> Choices { get; } = choices.ToList();

    /// <summary>The text for a choice. The default is <c>ToString()</c>.</summary>
    public Func<T, string> Label { get; set; } = item => item?.ToString() ?? string.Empty;

    /// <summary>How many choices are shown at once. The list scrolls past that.</summary>
    public int PageSize { get; set; } = 10;
}

/// <summary>Pick one of the choices. Type to filter the list, the arrow keys to move, Enter to choose, Esc to cancel.</summary>
public sealed class SelectionPrompt<T>(string question, IEnumerable<T> choices) : ChoicePrompt<T>(question, choices)
{
}

/// <summary>
/// Pick any number of the choices. Space marks or unmarks the highlighted choice, Enter confirms, Esc cancels. Enter is
/// refused until at least <see cref="MinimumSelections"/> choices are marked.
/// </summary>
public sealed class MultiSelectionPrompt<T>(string question, IEnumerable<T> choices) : ChoicePrompt<T>(question, choices)
{
    /// <summary>The fewest choices Enter accepts. Zero allows an empty selection.</summary>
    public int MinimumSelections { get; set; }
}
