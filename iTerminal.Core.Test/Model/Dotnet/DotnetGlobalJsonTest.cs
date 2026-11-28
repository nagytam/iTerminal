using iTerminal.Core.Model.Dotnet;

namespace iTerminal.Core.Test.Model.Dotnet;

[TestClass]
public class DotnetGlobalJsonTest
{
    private static DotnetSdk[] Installed(params string[] versions) => [.. versions.Select(version => new DotnetSdk(version, "/sdks"))];

    private static DotnetGlobalJson Pin(string version, string rollForward = "patch", bool allowPrerelease = false) =>
        new("/repo/global.json", version, rollForward, allowPrerelease);

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_ReadsTheVersionAndTheRollForwardPolicy()
    {
        DotnetGlobalJson? pin = DotnetGlobalJson.Parse("/repo/global.json", """
            {
              // the studio's SDK
              "sdk": { "version": "11.0.100", "rollForward": "latestFeature", "allowPrerelease": true }
            }
            """);

        Assert.IsNotNull(pin);
        Assert.AreEqual("11.0.100", pin.Version);
        Assert.AreEqual("latestFeature", pin.RollForward);
        Assert.IsTrue(pin.AllowPrerelease);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_WithoutARollForward_UsesPatch()
    {
        DotnetGlobalJson? pin = DotnetGlobalJson.Parse("/repo/global.json", """{ "sdk": { "version": "10.0.200" } }""");

        Assert.IsNotNull(pin);
        Assert.AreEqual("patch", pin.RollForward);
        Assert.IsFalse(pin.AllowPrerelease);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_WithoutAnSdkVersion_IsNull()
    {
        Assert.IsNull(DotnetGlobalJson.Parse("/repo/global.json", """{ "msbuild-sdks": { "Some.Sdk": "1.0.0" } }"""));
        Assert.IsNull(DotnetGlobalJson.Parse("/repo/global.json", "not json"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Find_TakesTheNearestGlobalJsonAboveTheFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "dotnet-global-" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(root, "src", "app");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(Path.Combine(root, "global.json"), """{ "sdk": { "version": "10.0.100" } }""");
            DotnetGlobalJson? found = DotnetGlobalJson.Find(nested);

            Assert.IsNotNull(found);
            Assert.AreEqual("10.0.100", found.Version);
            Assert.AreEqual(Path.Combine(root, "global.json"), found.FilePath);

            File.WriteAllText(Path.Combine(root, "src", "global.json"), """{ "sdk": { "version": "11.0.100" } }""");
            Assert.AreEqual("11.0.100", DotnetGlobalJson.Find(nested)!.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_Patch_TakesTheLowestPatchOfTheSameFeatureBand()
    {
        DotnetSdk? sdk = Pin("11.0.100").Match(Installed("11.0.105", "11.0.101", "11.0.200"));

        Assert.AreEqual("11.0.101", sdk!.Version);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_Patch_DoesNotRollToAnotherFeatureBand()
    {
        Assert.IsNull(Pin("11.0.100").Match(Installed("11.0.200", "10.0.100")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_LatestPatch_TakesTheNewestPatchOfTheBand()
    {
        DotnetSdk? sdk = Pin("11.0.100", "latestPatch").Match(Installed("11.0.100", "11.0.105", "11.0.200"));

        Assert.AreEqual("11.0.105", sdk!.Version);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_Feature_RollsToTheLowestHigherFeatureBand()
    {
        DotnetSdk? sdk = Pin("11.0.100", "feature").Match(Installed("10.0.200", "11.0.300", "11.0.201"));

        Assert.AreEqual("11.0.201", sdk!.Version);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_LatestFeature_TakesTheNewestSdkOfTheMinorVersion()
    {
        DotnetSdk? sdk = Pin("11.0.100", "latestFeature").Match(Installed("11.0.100", "11.0.201", "11.0.300", "10.0.300"));

        Assert.AreEqual("11.0.300", sdk!.Version);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_Minor_RollsAcrossMinorVersionsOfTheSameMajor()
    {
        DotnetSdk? sdk = Pin("11.0.100", "minor").Match(Installed("12.0.100", "11.1.100"));

        Assert.AreEqual("11.1.100", sdk!.Version);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_Major_RollsAcrossMajorVersions()
    {
        DotnetSdk? sdk = Pin("11.0.100", "major").Match(Installed("10.0.100", "12.0.100"));

        Assert.AreEqual("12.0.100", sdk!.Version);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_Disable_TakesOnlyTheExactVersion()
    {
        Assert.AreEqual("11.0.100", Pin("11.0.100", "disable").Match(Installed("11.0.101", "11.0.100"))!.Version);
        Assert.IsNull(Pin("11.0.100", "disable").Match(Installed("11.0.101")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_Prerelease_IsOnlyTakenWhenAllowedOrPinned()
    {
        DotnetSdk[] installed = Installed("11.0.101-preview.1");

        Assert.IsNull(Pin("11.0.100").Match(installed));
        Assert.AreEqual("11.0.101-preview.1", Pin("11.0.100", allowPrerelease: true).Match(installed)!.Version);
        Assert.AreEqual("11.0.101-preview.1", Pin("11.0.101-preview.1").Match(installed)!.Version);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_NothingInstalled_IsNull()
    {
        Assert.IsNull(Pin("11.0.100").Match([]));
    }
}
