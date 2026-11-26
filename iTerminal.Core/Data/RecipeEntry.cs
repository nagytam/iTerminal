namespace iTerminal.Core.Data;

/// <summary>A custom recipe the person saved. The ingredients and the steps are kept one per line.</summary>
public class RecipeEntry
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Origin { get; set; } = string.Empty;

    /// <summary>The ingredients, one per line.</summary>
    public string Ingredients { get; set; } = string.Empty;

    /// <summary>The steps, one per line.</summary>
    public string Steps { get; set; } = string.Empty;
}
