using iTerminal.Core.Data;
using iTerminal.Core.Model.Recipes;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Services.Recipes;

/// <summary>
/// Keeps the custom recipes in the <c>Recipes</c> table. The preloaded ones are not stored: they come from
/// <see cref="PreloadedRecipes"/>, and nothing here writes or deletes them.
/// </summary>
public sealed class RecipeService(IDbContextFactory<iTerminalDbContext> dbContextFactory) : IRecipeService
{
    public async Task<IReadOnlyList<Recipe>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await dbContext.Recipes.AsNoTracking().ToListAsync(cancellationToken);
        var custom = rows
            .OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(row => new Recipe(row.Id, row.Title, row.Origin, Recipe.Lines(row.Ingredients), Recipe.Lines(row.Steps), IsPreloaded: false));
        return [.. PreloadedRecipes.All, .. custom];
    }

    public async Task SaveAsync(Recipe recipe, CancellationToken cancellationToken = default)
    {
        if (PreloadedRecipes.IsPreloaded(recipe.Id))
        {
            throw new InvalidOperationException("Preloaded recipes cannot be changed.");
        }

        // Checked and stored as trimmed, so a blank ingredient cannot pass the "at least one" rule and come back empty.
        recipe = recipe.Trimmed();
        if (recipe.Check() is { } problem)
        {
            throw new ArgumentException($"The recipe is not valid: {problem}.", nameof(recipe));
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entry = await dbContext.Recipes.FindAsync([recipe.Id], cancellationToken);
        if (entry is null)
        {
            entry = new RecipeEntry { Id = recipe.Id };
            dbContext.Recipes.Add(entry);
        }

        entry.Title = recipe.Title;
        entry.Origin = recipe.Origin;
        entry.Ingredients = string.Join('\n', recipe.Ingredients);
        entry.Steps = string.Join('\n', recipe.Steps);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (PreloadedRecipes.IsPreloaded(id))
        {
            return false;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entry = await dbContext.Recipes.FindAsync([id], cancellationToken);
        if (entry is null)
        {
            return false;
        }

        dbContext.Recipes.Remove(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
