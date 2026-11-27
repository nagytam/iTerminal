using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class DurationEstimatorTest
{
    private static readonly DevelopmentProject App = new("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/App/App.csproj");
    private static readonly DevelopmentProject Lib = new("Lib", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/Lib/Lib.csproj");
    private static readonly DevelopmentProject Script = new("tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, "/src/tool.py");

    [Timeout(60_000)]
    [TestMethod]
    public void TheFirstMeasurementIsTheEstimate()
    {
        var estimator = new DurationEstimator();

        estimator.Record("build", App, TimeSpan.FromSeconds(10), utcTicks: 1);

        Assert.AreEqual(TimeSpan.FromSeconds(10), estimator.Estimate("build", App));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LaterMeasurementsMoveTheEstimateBySmoothing()
    {
        var estimator = new DurationEstimator();
        estimator.Record("build", App, TimeSpan.FromSeconds(10), utcTicks: 1);

        estimator.Record("build", App, TimeSpan.FromSeconds(20), utcTicks: 2);

        // 10 + 0.3 * (20 - 10): the recent run counts for a share, not for all of it.
        Assert.AreEqual(13, estimator.Estimate("build", App)!.Value.TotalSeconds, 0.001);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnItemWithoutRunsUsesTheEstimateOfItsLanguageAndKind()
    {
        var estimator = new DurationEstimator();
        estimator.Record("test", App, TimeSpan.FromSeconds(8), utcTicks: 1);

        Assert.AreEqual(TimeSpan.FromSeconds(8), estimator.Estimate("test", Lib), "Lib is a .NET project, as App is");
        Assert.IsNull(estimator.Estimate("test", Script), "a Python script is not measured by a .NET project's runs");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VerbsDoNotShareEstimates()
    {
        var estimator = new DurationEstimator();
        estimator.Record("build", App, TimeSpan.FromSeconds(10), utcTicks: 1);

        Assert.IsNull(estimator.Estimate("test", App));
        Assert.IsNull(estimator.Estimate("clean", App));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheEstimatesSurviveAnEncodeAndDecode()
    {
        var estimator = new DurationEstimator();
        estimator.Record("build", App, TimeSpan.FromSeconds(10), utcTicks: 1);
        estimator.Record("build", App, TimeSpan.FromSeconds(20), utcTicks: 2);

        var decoded = DurationEstimator.Decode(estimator.Encode());

        Assert.AreEqual(estimator.Count, decoded.Count);
        Assert.AreEqual(13, decoded.Estimate("build", App)!.Value.TotalSeconds, 0.001);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMissingOrDamagedValueGivesNoEstimates()
    {
        Assert.AreEqual(0, DurationEstimator.Decode(null).Count);
        Assert.AreEqual(0, DurationEstimator.Decode("not json").Count);
        Assert.AreEqual(0, DurationEstimator.Decode("{\"build|x\":{\"Seconds\":-5,\"Samples\":1,\"Updated\":1}}").Count, "a negative estimate is dropped");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PastTheLimitTheLeastRecentlyMeasuredEntriesAreDropped()
    {
        var estimator = new DurationEstimator();
        var items = Enumerable.Range(0, DurationEstimator.MaxEntries).Select(i => new DevelopmentProject($"P{i}", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, $"/src/P{i}/P{i}.csproj")).ToList();
        for (int i = 0; i < items.Count; i++) estimator.Record("build", items[i], TimeSpan.FromSeconds(1), utcTicks: i + 1);

        // Each run adds its own entry and the entry of its kind: 301 entries, so the least recently measured one (P0's own) goes.
        Assert.AreEqual(DurationEstimator.MaxEntries, estimator.Count);
        Assert.AreEqual(DurationEstimator.MaxEntries, DurationEstimator.Decode(estimator.Encode()).Count);
        Assert.IsNotNull(estimator.Estimate("build", items[0]), "P0 falls back to the estimate of its kind");
        Assert.IsNotNull(estimator.Estimate("build", items[^1]), "the newest item keeps its own estimate");
    }
}
