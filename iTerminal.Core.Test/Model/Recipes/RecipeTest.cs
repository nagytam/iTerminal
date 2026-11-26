using iTerminal.Core.Model.Recipes;

namespace iTerminal.Core.Test.Model.Recipes;

[TestClass]
public class RecipeTest
{
    private static Recipe Valid(string title = "Omelette", string origin = "France", string[]? ingredients = null, string[]? steps = null) =>
        new(Guid.NewGuid(), title, origin, ingredients ?? ["2 eggs", "butter"], steps ?? ["Whisk.", "Fry."], IsPreloaded: false);

    [TestMethod]
    public void AValidRecipeHasNoProblem()
    {
        Assert.IsNull(Valid().Check());
    }

    [TestMethod]
    public void ATitleIsRequired()
    {
        Assert.AreEqual(RecipeProblem.TitleRequired, Valid(title: string.Empty).Check());
        Assert.AreEqual(RecipeProblem.TitleRequired, Valid(title: "   ").Check());
    }

    [TestMethod]
    public void AtLeastOneIngredientAndOneStepAreRequired()
    {
        Assert.AreEqual(RecipeProblem.IngredientRequired, Valid(ingredients: []).Check());
        Assert.AreEqual(RecipeProblem.StepRequired, Valid(steps: []).Check());
    }

    [TestMethod]
    [DataRow("title")]
    [DataRow("origin")]
    [DataRow("ingredient")]
    [DataRow("step")]
    public void ALineBreakInAnyTextFieldIsRefused(string field)
    {
        var recipe = field switch
        {
            "title" => Valid(title: "Egg\nsalad"),
            "origin" => Valid(origin: "Fra\rnce"),
            "ingredient" => Valid(ingredients: ["2 eggs\nand butter"]),
            _ => Valid(steps: ["Whisk\r\nthen fry"]),
        };

        Assert.AreEqual(RecipeProblem.LineBreak, recipe.Check());
    }

    [TestMethod]
    public void TheLengthLimitsAreChecked()
    {
        Assert.AreEqual(RecipeProblem.TooLong, Valid(title: new string('t', Recipe.MaxTitleLength + 1)).Check());
        Assert.AreEqual(RecipeProblem.TooLong, Valid(origin: new string('o', Recipe.MaxOriginLength + 1)).Check());
        Assert.AreEqual(RecipeProblem.TooLong, Valid(ingredients: [new string('i', Recipe.MaxLineLength + 1)]).Check());
        Assert.AreEqual(RecipeProblem.TooLong, Valid(steps: [new string('s', Recipe.MaxLineLength + 1)]).Check());
        Assert.IsNull(Valid(title: new string('t', Recipe.MaxTitleLength)).Check());
    }

    [TestMethod]
    public void TheItemCountLimitIsChecked()
    {
        var hundred = Enumerable.Repeat("x", Recipe.MaxItems).ToArray();
        Assert.IsNull(Valid(ingredients: hundred, steps: hundred).Check());
        Assert.AreEqual(RecipeProblem.TooMany, Valid(ingredients: [.. hundred, "x"]).Check());
        Assert.AreEqual(RecipeProblem.TooMany, Valid(steps: [.. hundred, "x"]).Check());
    }

    [TestMethod]
    public void LinesDropsBlankLinesAndTrims()
    {
        CollectionAssert.AreEqual(new[] { "2 eggs", "butter" }, Recipe.Lines("  2 eggs \n\n   \r\nbutter\n").ToList());
        CollectionAssert.AreEqual(Array.Empty<string>(), Recipe.Lines("\n \n").ToList());
    }

    [TestMethod]
    public void TrimmedTrimsTheTextAndDropsBlankItems()
    {
        var trimmed = new Recipe(Guid.NewGuid(), "  Omelette  ", " France ", ["2 eggs", " ", " butter "], ["Fry."], IsPreloaded: false).Trimmed();

        Assert.AreEqual("Omelette", trimmed.Title);
        Assert.AreEqual("France", trimmed.Origin);
        CollectionAssert.AreEqual(new[] { "2 eggs", "butter" }, trimmed.Ingredients.ToList());
        Assert.IsNull(trimmed.Check());
    }
}
