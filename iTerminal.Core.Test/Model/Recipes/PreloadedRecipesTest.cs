using iTerminal.Core.Model.Recipes;

namespace iTerminal.Core.Test.Model.Recipes;

[TestClass]
public class PreloadedRecipesTest
{
    [TestMethod]
    public void TwoFavouritesFromEachOfFiveCountries()
    {
        var byOrigin = PreloadedRecipes.All.GroupBy(recipe => recipe.Origin).ToDictionary(group => group.Key, group => group.Count());

        CollectionAssert.AreEquivalent(new[] { "Switzerland", "Germany", "France", "Italy", "Hungary" }, byOrigin.Keys.ToList());
        Assert.IsTrue(byOrigin.Values.All(count => count == 2), "two recipes per country");
    }

    [TestMethod]
    public void EveryPreloadedRecipeIsValidAndMarkedAsPreloaded()
    {
        foreach (var recipe in PreloadedRecipes.All)
        {
            Assert.IsTrue(recipe.IsPreloaded, recipe.Title);
            Assert.IsNull(recipe.Check(), recipe.Title);
            Assert.IsTrue(PreloadedRecipes.IsPreloaded(recipe.Id), recipe.Title);
        }
    }

    [TestMethod]
    public void TheIdentifiersAreUnique()
    {
        Assert.AreEqual(PreloadedRecipes.All.Count, PreloadedRecipes.All.Select(recipe => recipe.Id).Distinct().Count());
    }
}
