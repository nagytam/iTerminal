namespace iTerminal.Core.Model.Recipes;

/// <summary>What is wrong with a recipe the person typed. The dialog words it, so the message follows the language.</summary>
public enum RecipeProblem
{
    TitleRequired,
    IngredientRequired,
    StepRequired,
    LineBreak,
    TooLong,
    TooMany,
}

/// <summary>
/// A recipe: a title, the country or region it comes from, its ingredients and its steps, one item per list entry.
/// Immutable; a change is a copy. <see cref="IsPreloaded"/> marks the recipes that ship with the application, which
/// are read-only. The limits are checked here, so the database columns and the dialog never disagree with them.
/// </summary>
public sealed record Recipe(
    Guid Id,
    string Title,
    string Origin,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Steps,
    bool IsPreloaded)
{
    public const int MaxTitleLength = 256;
    public const int MaxOriginLength = 128;
    public const int MaxLineLength = 1000;
    public const int MaxItems = 100;

    /// <summary>
    /// The first problem with the recipe as it is, or null when it can be saved. Trim and drop blank items with
    /// <see cref="Trimmed"/> first. A line break anywhere in the text is a problem: the ingredients and steps are stored
    /// one per line, so a break inside one would not come back as it was saved.
    /// </summary>
    public RecipeProblem? Check()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return RecipeProblem.TitleRequired;
        }

        if (HasLineBreak(Title) || HasLineBreak(Origin) || Ingredients.Any(HasLineBreak) || Steps.Any(HasLineBreak))
        {
            return RecipeProblem.LineBreak;
        }

        if (Title.Length > MaxTitleLength || Origin.Length > MaxOriginLength || Ingredients.Concat(Steps).Any(line => line.Length > MaxLineLength))
        {
            return RecipeProblem.TooLong;
        }

        if (Ingredients.Count > MaxItems || Steps.Count > MaxItems)
        {
            return RecipeProblem.TooMany;
        }

        if (Ingredients.Count == 0)
        {
            return RecipeProblem.IngredientRequired;
        }

        if (Steps.Count == 0)
        {
            return RecipeProblem.StepRequired;
        }

        return null;
    }

    /// <summary>The same recipe with the title and origin trimmed, and the blank ingredients and steps dropped.</summary>
    public Recipe Trimmed() => this with
    {
        Title = Title.Trim(),
        Origin = Origin.Trim(),
        Ingredients = Clean(Ingredients),
        Steps = Clean(Steps),
    };

    /// <summary>Splits text typed one item per line into the list the recipe keeps, blank lines dropped.</summary>
    public static IReadOnlyList<string> Lines(string text) =>
        Clean(text.Replace("\r\n", "\n").Split('\n'));

    private static List<string> Clean(IEnumerable<string> items) =>
        items.Select(item => item.Trim()).Where(item => item.Length > 0).ToList();

    private static bool HasLineBreak(string text) => text.Contains('\n') || text.Contains('\r');
}
