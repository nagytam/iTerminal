using iTerminal.Core.Services;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class RuntimeDependencyServiceTest
{
    private static RuntimeDependencyService NewService(ISettingsService settings, FakeRuntimeDependency dependency) =>
        new(settings, [dependency]);

    [TestMethod]
    public async Task OnlyTheDependenciesNamedAreInstalled()
    {
        var first = new FakeRuntimeDependency("1.63.0", "first");
        var second = new FakeRuntimeDependency("2.0.0", "second");
        var service = new RuntimeDependencyService(new SettingsService(), [first, second]);

        await service.InstallAsync(CancellationToken.None, ["second"]);

        Assert.AreEqual(0, first.Installs.Count);
        CollectionAssert.AreEqual(new[] { "2.0.0" }, second.Installs);
        Assert.AreEqual(RuntimeDependencyState.Missing, service.Statuses.Single(status => status.Id == "first").State);
    }

    [TestMethod]
    public void OnlyTheDependenciesNamedAreSkipped()
    {
        var settings = new SettingsService();
        var service = new RuntimeDependencyService(
            settings,
            [new FakeRuntimeDependency("1.63.0", "first"), new FakeRuntimeDependency("2.0.0", "second")]);

        service.SkipOffers(["first"]);

        Assert.AreEqual("1.63.0", settings.Get(RuntimeDependencyService.SkippedSettingPrefix + "first"));
        Assert.IsNull(settings.Get(RuntimeDependencyService.SkippedSettingPrefix + "second"));
        Assert.IsTrue(service.ShouldOfferAtStartup, "The one not skipped is still offered.");
    }

    [TestMethod]
    public void AMissingDependencyIsOfferedAtTheVersionTheBuildWasCompiledAgainst()
    {
        var dependency = new FakeRuntimeDependency("1.63.0");
        var service = NewService(new SettingsService(), dependency);

        RuntimeDependencyStatus status = service.Statuses.Single();

        Assert.AreEqual(RuntimeDependencyState.Missing, status.State);
        Assert.AreEqual("1.63.0", status.TargetVersion);
        Assert.IsTrue(service.ShouldOfferAtStartup);
        Assert.AreEqual(1, dependency.Activations, "The installed version is pointed at when the service starts.");
    }

    [TestMethod]
    public async Task AMissingDependencyIsInstalledAtTheNewestVersionOnItsLine()
    {
        var dependency = new FakeRuntimeDependency("1.63.0") { Published = ["1.63.0", "1.63.4", "1.64.0"] };
        var service = NewService(new SettingsService(), dependency);

        await service.CheckAsync(CancellationToken.None);

        Assert.AreEqual(RuntimeDependencyState.Missing, service.Statuses.Single().State);
        Assert.AreEqual("1.63.4", service.Statuses.Single().TargetVersion);
    }

    [TestMethod]
    public async Task AnUpToDateDependencyIsNotOffered()
    {
        var dependency = new FakeRuntimeDependency("1.63.0") { Installed = "1.63.0", Published = ["1.62.0", "1.63.0"] };
        var service = NewService(new SettingsService(), dependency);

        await service.CheckAsync(CancellationToken.None);

        Assert.AreEqual(RuntimeDependencyState.Installed, service.Statuses.Single().State);
        Assert.IsFalse(service.ShouldOfferAtStartup);
    }

    [TestMethod]
    public async Task ANewerVersionOnTheSameLineIsAnUpdateAndALaterLineIsOnlyANote()
    {
        var dependency = new FakeRuntimeDependency("1.63.0")
        {
            Installed = "1.63.0",
            Published = ["1.63.0", "1.63.2", "1.63.3-beta.1", "1.64.0"],
        };
        var service = NewService(new SettingsService(), dependency);

        await service.CheckAsync(CancellationToken.None);

        RuntimeDependencyStatus status = service.Statuses.Single();
        Assert.AreEqual(RuntimeDependencyState.UpdateAvailable, status.State);
        Assert.AreEqual("1.63.2", status.TargetVersion, "A pre-release is not offered, and 1.64 is another line.");
        Assert.IsNotNull(status.Note);
        StringAssert.Contains(status.Note, "1.64.0");
        StringAssert.Contains(status.Note, "uses 1.63.0");
        Assert.IsTrue(service.ShouldOfferAtStartup);
    }

    [TestMethod]
    public async Task SkippingAnOfferHidesItUntilANewerVersionAppears()
    {
        var settings = new SettingsService();
        var dependency = new FakeRuntimeDependency("1.63.0") { Installed = "1.63.0", Published = ["1.63.1"] };
        var service = NewService(settings, dependency);
        await service.CheckAsync(CancellationToken.None);
        Assert.IsTrue(service.ShouldOfferAtStartup);

        service.SkipOffers();

        Assert.IsFalse(service.ShouldOfferAtStartup);
        Assert.IsFalse(NewService(settings, dependency).ShouldOfferAtStartup, "The skip is remembered by the next start.");

        dependency.Published = ["1.63.1", "1.63.2"];
        await service.CheckAsync(CancellationToken.None);
        Assert.IsTrue(service.ShouldOfferAtStartup, "A version newer than the skipped one is offered again.");
    }

    [TestMethod]
    public void AVersionSeenByAnEarlierRunIsOfferedWithoutTheNetwork()
    {
        var settings = new SettingsService();
        settings.Set(RuntimeDependencyService.LatestSettingPrefix + "fake", "1.63.2");
        var dependency = new FakeRuntimeDependency("1.63.0") { Installed = "1.63.0" };

        var service = NewService(settings, dependency);

        Assert.AreEqual(RuntimeDependencyState.UpdateAvailable, service.Statuses.Single().State);
        Assert.AreEqual("1.63.2", service.Statuses.Single().TargetVersion);
    }

    [TestMethod]
    public async Task AFailedCheckKeepsWhatIsInstalledAndSaysWhy()
    {
        var dependency = new FakeRuntimeDependency("1.63.0")
        {
            Installed = "1.63.0",
            ListFailure = new HttpRequestException("the network is unreachable"),
        };
        var service = NewService(new SettingsService(), dependency);

        await service.CheckAsync(CancellationToken.None);

        RuntimeDependencyStatus status = service.Statuses.Single();
        Assert.AreEqual(RuntimeDependencyState.Installed, status.State);
        StringAssert.Contains(status.Problem, "the network is unreachable");
        Assert.IsFalse(service.ShouldOfferAtStartup);
    }

    [TestMethod]
    public async Task InstallingAMissingDependencyInstallsTheBuildsVersionAndLogsIt()
    {
        var dependency = new FakeRuntimeDependency("1.63.0");
        var service = NewService(new SettingsService(), dependency);

        await service.InstallAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "1.63.0" }, dependency.Installs);
        Assert.AreEqual(RuntimeDependencyState.Installed, service.Statuses.Single().State);
        Assert.IsFalse(service.IsBusy);
        CollectionAssert.Contains(service.Log.ToList(), "Fake: installing 1.63.0");
        CollectionAssert.Contains(service.Log.ToList(), "fetching 1.63.0");
        Assert.IsTrue(service.IsInstalled("fake"));
    }

    [TestMethod]
    public async Task AFailedInstallIsLoggedAndDoesNotEscape()
    {
        var dependency = new FakeRuntimeDependency("1.63.0") { InstallFailure = new IOException("disk is full") };
        var service = NewService(new SettingsService(), dependency);

        await service.InstallAsync(CancellationToken.None);

        Assert.AreEqual(RuntimeDependencyState.Missing, service.Statuses.Single().State);
        Assert.IsFalse(service.IsBusy, "A failed install releases the busy flag.");
        Assert.IsTrue(service.Log.Any(line => line.Contains("could not be installed: disk is full")));
    }

    [TestMethod]
    public void InvalidatingAnInstalledDependencyReportsItMissing()
    {
        var dependency = new FakeRuntimeDependency("1.63.0") { Installed = "1.63.0" };
        var service = NewService(new SettingsService(), dependency);

        service.InvalidateInstalled("fake");

        Assert.AreEqual(1, dependency.Invalidations);
        Assert.AreEqual(RuntimeDependencyState.Missing, service.Statuses.Single().State);
        Assert.IsTrue(service.ShouldOfferAtStartup);
    }
}
