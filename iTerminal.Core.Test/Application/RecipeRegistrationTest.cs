using iConsole.Application;
using iConsole.Data;
using iTerminal.Core.Application;
using iTerminal.Core.Dialogs.Recipes;
using iTerminal.Core.Services.Recipes;
using iTerminal.Core.Test.Helper;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Core.Test.Application;

/// <summary>
/// Resolves the recipe manager out of the application's real container. The manager takes a factory for the edit form, so a
/// missing registration of either would compile and only fail when the menu item opens; this test fails instead.
/// </summary>
[TestClass]
public class RecipeRegistrationTest
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDefaultConsoleDriver();
        services.AddiConsoleCore();
        services.AddiTerminal(TemporaryDataDirectory.Create());
        return services.BuildServiceProvider();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_ResolvesTheRecipeManagerAndItsEditFormFactory()
    {
        using var provider = BuildProvider();

        Assert.IsInstanceOfType<RecipeService>(provider.GetRequiredService<IRecipeService>());
        Assert.IsNotNull(provider.GetRequiredService<RecipeManagerDialog>());
        Assert.IsNotNull(provider.GetRequiredService<RecipeEditDialog>());
        var newEditDialog = provider.GetRequiredService<Func<RecipeEditDialog>>();
        Assert.IsNotNull(newEditDialog());
    }
}
