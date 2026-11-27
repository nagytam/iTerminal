using iTerminal.Core.Model.Recipes;

namespace iTerminal.Core.Services.Recipes;

/// <summary>
/// The recipes the person can browse: the preloaded ones from code, then the custom ones saved in the database. Only custom
/// recipes can be saved or deleted.
/// </summary>
public interface IRecipeService
{
    /// <summary>The preloaded recipes, then the custom ones by title.</summary>
    Task<IReadOnlyList<Recipe>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a custom recipe, adding it or replacing the one with the same identifier. A preloaded recipe is refused
    /// with <see cref="InvalidOperationException"/>, and an invalid one with <see cref="ArgumentException"/>.
    /// </summary>
    Task SaveAsync(Recipe recipe, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom recipe. Returns false when there is no such custom recipe, including a preloaded one.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
