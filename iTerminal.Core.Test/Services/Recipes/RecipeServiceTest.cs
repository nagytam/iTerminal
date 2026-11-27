using iTerminal.Core.Data;
using iTerminal.Core.Model.Recipes;
using iTerminal.Core.Services.Recipes;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services.Recipes;

[TestClass]
public class RecipeServiceTest
{
    private string _folder = string.Empty;
    private TestDbContextFactory _factory = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "RecipeServiceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
        _factory = new TestDbContextFactory(Path.Combine(_folder, "test.sqlite"));
        await using var context = _factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Windows will not delete a SQLite file while a pooled connection to it is open: release the pool first.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private static Recipe Custom(string title, string[] ingredients, string[] steps, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), title, "Testland", ingredients, steps, IsPreloaded: false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoCustomRecipesTheListIsThePreloadedOnes()
    {
        var recipes = await new RecipeService(_factory).GetAllAsync();

        CollectionAssert.AreEqual(PreloadedRecipes.All.Select(r => r.Id).ToList(), recipes.Select(r => r.Id).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASavedRecipeIsListedAfterThePreloadedOnesAndKeepsItsLines()
    {
        var service = new RecipeService(_factory);
        var recipe = Custom("Grandma's soup", ["1 onion", "2 carrots"], ["Simmer for an hour.", "Serve hot."]);

        await service.SaveAsync(recipe);
        var all = await service.GetAllAsync();

        var saved = all.Last();
        Assert.AreEqual(recipe.Id, saved.Id);
        Assert.AreEqual("Grandma's soup", saved.Title);
        Assert.IsFalse(saved.IsPreloaded);
        CollectionAssert.AreEqual(new[] { "1 onion", "2 carrots" }, saved.Ingredients.ToList());
        CollectionAssert.AreEqual(new[] { "Simmer for an hour.", "Serve hot." }, saved.Steps.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SavingTheSameIdentifierReplacesTheRecipe()
    {
        var service = new RecipeService(_factory);
        var id = Guid.NewGuid();
        await service.SaveAsync(Custom("First", ["a"], ["b"], id));

        await service.SaveAsync(Custom("Second", ["c"], ["d"], id));

        var custom = (await service.GetAllAsync()).Where(r => !r.IsPreloaded).ToList();
        Assert.AreEqual(1, custom.Count);
        Assert.AreEqual("Second", custom[0].Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheRecipeIsKeptInTheDatabaseAndReadBackByAFreshService()
    {
        await new RecipeService(_factory).SaveAsync(Custom("Kept", ["flour"], ["Mix."]));

        var fresh = await new RecipeService(_factory).GetAllAsync();

        Assert.IsTrue(fresh.Any(r => r.Title == "Kept" && !r.IsPreloaded));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeletingACustomRecipeRemovesIt()
    {
        var service = new RecipeService(_factory);
        var recipe = Custom("Temporary", ["x"], ["y"]);
        await service.SaveAsync(recipe);

        Assert.IsTrue(await service.DeleteAsync(recipe.Id));

        Assert.IsFalse((await service.GetAllAsync()).Any(r => r.Id == recipe.Id));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APreloadedRecipeCannotBeDeletedOrOverwritten()
    {
        var service = new RecipeService(_factory);
        var preloaded = PreloadedRecipes.All[0];

        Assert.IsFalse(await service.DeleteAsync(preloaded.Id));
        await ExpectAsync<InvalidOperationException>(() => service.SaveAsync(preloaded with { Title = "Changed" }));

        Assert.IsTrue((await service.GetAllAsync()).Any(r => r.Id == preloaded.Id && r.Title == preloaded.Title));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInvalidRecipeIsNotSaved()
    {
        var service = new RecipeService(_factory);

        await ExpectAsync<ArgumentException>(() => service.SaveAsync(Custom(string.Empty, ["a"], ["b"])));

        Assert.AreEqual(PreloadedRecipes.All.Count, (await service.GetAllAsync()).Count);
    }

    private static async Task ExpectAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private sealed class TestDbContextFactory(string databasePath) : Microsoft.EntityFrameworkCore.IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options =
            new DbContextOptionsBuilder<iTerminalDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

        public iTerminalDbContext CreateDbContext() => new(_options);

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
