using iConsole.Localization;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands;

/// <summary>A command that shows a dialog builds a fresh one when the language has changed since the dialog was built.</summary>
[TestClass]
[DoNotParallelize]
public class ShowDialogCommandLanguageTest
{
    private IDisposable? _language;
    private IServiceProvider? _before;

    [TestInitialize]
    public void Remember() => _before = DialogRefresh.Services;

    [TestCleanup]
    public void Reset()
    {
        _language?.Dispose();
        DialogRefresh.Services = _before;
    }

    private static Localizer Localizer(string language)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(AboutDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(language));
        return localizer;
    }

    private sealed class ProbeCommand(AboutDialog dialog) : ShowDialogCommand<AboutDialog>(dialog)
    {
        public AboutDialog Current => Dialog;

        public bool Refresh() => RefreshDialogForLanguage();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALanguageChangedSinceTheDialogWasBuilt_GivesTheCommandAFreshDialog_OnlyOnce()
    {
        ServiceCollection services = new();
        services.AddTransient(_ => new AboutDialog(TestTheme()));
        DialogRefresh.Services = services.BuildServiceProvider();
        _language = UiText.Scope(Localizer("en"));
        AboutDialog built = new(TestTheme());
        ProbeCommand command = new(built);

        Assert.IsFalse(command.Refresh(), "same language: nothing to do");
        Assert.AreSame(built, command.Current);

        _language.Dispose();
        _language = UiText.Scope(Localizer("de"));

        Assert.IsTrue(command.Refresh());
        Assert.AreNotSame(built, command.Current);
        AboutDialog fresh = command.Current;
        Assert.IsFalse(command.Refresh(), "already refreshed for German");
        Assert.AreSame(fresh, command.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutServices_TheCommandKeepsItsDialog()
    {
        DialogRefresh.Services = null;
        _language = UiText.Scope(Localizer("en"));
        AboutDialog built = new(TestTheme());
        ProbeCommand command = new(built);
        _language.Dispose();
        _language = UiText.Scope(Localizer("de"));

        Assert.IsFalse(command.Refresh());
        Assert.AreSame(built, command.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhenTheHostStops_TheServicesAreReleased_ButOnlyIfTheyAreStillTheOnesItRegistered()
    {
        IServiceProvider first = new ServiceCollection().BuildServiceProvider();
        IServiceProvider second = new ServiceCollection().BuildServiceProvider();
        DialogRefreshHostedService service = new(first);

        await service.StartAsync(CancellationToken.None);
        Assert.AreSame(first, DialogRefresh.Services);
        await service.StopAsync(CancellationToken.None);
        Assert.IsNull(DialogRefresh.Services, "a stopped application must not stay reachable from a static");

        await service.StartAsync(CancellationToken.None);
        DialogRefresh.Services = second;
        await service.StopAsync(CancellationToken.None);
        Assert.AreSame(second, DialogRefresh.Services, "a newer application's services are not ours to release");
    }
}
