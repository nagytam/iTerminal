using iConsole.Services;
using iTerminal.Core.Dialogs.Recipes;
using iTerminal.Core.Model.Recipes;
using iTerminal.Core.Services.Recipes;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Recipes;

[TestClass]
public class RecipeManagerDialogTest
{
    private IDisposable _clipboardScope = null!;

    [TestCleanup]
    public void ReleaseOwnClipboard() => _clipboardScope.Dispose();

    [TestInitialize]
    public void ScopeOwnClipboard() => _clipboardScope = TextClipboard.Scope(new InProcessTextClipboardService());

    /// <summary>An in-memory recipe service: the preloaded recipes plus the custom ones the test saves.</summary>
    private sealed class FakeRecipeService : IRecipeService
    {
        public List<Recipe> Custom { get; } = [];

        public Task<IReadOnlyList<Recipe>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Recipe>>([.. PreloadedRecipes.All, .. Custom.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)]);

        public Task SaveAsync(Recipe recipe, CancellationToken cancellationToken = default)
        {
            Custom.RemoveAll(r => r.Id == recipe.Id);
            Custom.Add(recipe);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Custom.RemoveAll(r => r.Id == id) > 0);
    }

    private sealed class Fixture
    {
        public required RecipeManagerDialog Manager { get; init; }
        public required RecipeEditDialog Edit { get; init; }
        public required FakeRecipeService Service { get; init; }
    }

    private static Fixture Create()
    {
        var theme = TestTheme();
        var edit = new RecipeEditDialog(new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, theme);
        var service = new FakeRecipeService();
        return new Fixture { Manager = new RecipeManagerDialog(service, () => edit, theme), Edit = edit, Service = service };
    }

    /// <summary>Shows the manager, waits for the read it starts, and applies it the way the idle pass would.</summary>
    private static async Task OpenAsync(Fixture fixture)
    {
        await ShowAsync(fixture.Manager);
        await fixture.Manager.LoadTask;
        fixture.Manager.OnIdle();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensWithThePreloadedRecipesAndTheFirstOnesIngredients()
    {
        var fixture = Create();

        await OpenAsync(fixture);

        Assert.AreEqual(PreloadedRecipes.All.Count, fixture.Manager.RecipesForTest.Count);
        var screen = Render(fixture.Manager);
        AssertScreenContains(screen, "Rösti");
        AssertScreenContains(screen, "1 kg waxy potatoes");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EOnAPreloadedRecipeSaysItCannotBeChanged()
    {
        var fixture = Create();
        await OpenAsync(fixture);

        await fixture.Manager.HandleKeyEventAsync(Char('e'));

        AssertScreenContains(Render(fixture.Manager), "Preloaded recipes cannot be changed.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DOnAPreloadedRecipeSaysItCannotBeChangedAndAsksNothing()
    {
        var fixture = Create();
        await OpenAsync(fixture);

        await fixture.Manager.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        Assert.IsFalse(fixture.Manager.ConfirmingDeleteForTest);
        var screen = Render(fixture.Manager);
        AssertScreenContains(screen, "Preloaded recipes cannot be changed.");
        AssertScreenDoesNotContain(screen, "(y/n)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NOpensAnEmptyEditFormAndSavingItReturnsToTheListWithTheRecipe()
    {
        var fixture = Create();
        await OpenAsync(fixture);

        await fixture.Manager.HandleKeyEventAsync(Char('n'));
        Assert.AreEqual(string.Empty, fixture.Edit.TitleForTest);
        fixture.Edit.TitleForTest = "Test soup";
        fixture.Edit.OriginForTest = "Testland";
        fixture.Edit.IngredientsEditorForTest.TextModel.Lines = ["1 onion"];
        fixture.Edit.StepsEditorForTest.TextModel.Lines = ["Simmer."];
        fixture.Edit.Save();
        await fixture.Manager.OperationTask;
        fixture.Manager.OnIdle();

        Assert.AreEqual(1, fixture.Service.Custom.Count);
        Assert.AreEqual(PreloadedRecipes.All.Count + 1, fixture.Manager.RecipesForTest.Count);
        AssertScreenContains(Render(fixture.Manager), "Recipe saved.");
        // The new recipe sorts below the six rows the table shows, so it is checked on the list, not the screen.
        Assert.IsTrue(fixture.Manager.RecipesForTest.Any(r => r.Title == "Test soup" && !r.IsPreloaded));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DOnACustomRecipeAsksFirstAndAWrongKeyWithdrawsTheQuestion()
    {
        var fixture = Create();
        fixture.Service.Custom.Add(new Recipe(Guid.NewGuid(), "Zebra cake", "", ["flour"], ["Bake."], IsPreloaded: false));
        await OpenAsync(fixture);
        await fixture.Manager.HandleKeyEventAsync(Key(ConsoleKey.End));

        await fixture.Manager.HandleKeyEventAsync(Char('d'));
        AssertScreenContains(Render(fixture.Manager), "Delete \"Zebra cake\"? (y/n)");

        await fixture.Manager.HandleKeyEventAsync(Char('x'));

        Assert.IsFalse(fixture.Manager.ConfirmingDeleteForTest);
        Assert.AreEqual(1, fixture.Service.Custom.Count);
        AssertScreenDoesNotContain(Render(fixture.Manager), "(y/n)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task YOnACustomRecipeDeletesItAndSaysSo()
    {
        var fixture = Create();
        fixture.Service.Custom.Add(new Recipe(Guid.NewGuid(), "Zebra cake", "", ["flour"], ["Bake."], IsPreloaded: false));
        await OpenAsync(fixture);
        await fixture.Manager.HandleKeyEventAsync(Key(ConsoleKey.End));

        await fixture.Manager.HandleKeyEventAsync(Char('d'));
        await fixture.Manager.HandleKeyEventAsync(Char('y'));
        await fixture.Manager.OperationTask;
        fixture.Manager.OnIdle();

        Assert.AreEqual(0, fixture.Service.Custom.Count);
        Assert.AreEqual(PreloadedRecipes.All.Count, fixture.Manager.RecipesForTest.Count);
        AssertScreenContains(Render(fixture.Manager), "Recipe deleted.");
    }
}
