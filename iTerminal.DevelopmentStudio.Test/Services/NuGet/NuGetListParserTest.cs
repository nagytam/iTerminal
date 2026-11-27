using iTerminal.DevelopmentStudio.Services.NuGet;

namespace iTerminal.DevelopmentStudio.Test.Services.NuGet;

/// <summary>The JSON of <c>dotnet list package --format json</c> is shaped as the SDK prints it (captured from SDK 11).</summary>
[TestClass]
public class NuGetListParserTest
{
    private static readonly string AppProject = Path.GetFullPath("C:/src/App/App.csproj");

    /// <summary>The project paths come with forward slashes, as <c>dotnet list</c> prints them on Windows.</summary>
    private static string Installed(string path = "C:/src/App/App.csproj") => $$"""
        {
          "version": 1,
          "parameters": "",
          "projects": [
            {
              "path": "{{path}}",
              "frameworks": [
                {
                  "framework": "net8.0",
                  "topLevelPackages": [
                    { "id": "Probe.Lib", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static string Outdated(string latest, string path = "C:/src/App/App.csproj") => $$"""
        {
          "version": 1,
          "parameters": "--outdated",
          "sources": [ "C:/feed" ],
          "projects": [
            {
              "path": "{{path}}",
              "frameworks": [
                {
                  "framework": "net8.0",
                  "topLevelPackages": [
                    { "id": "Probe.Lib", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0", "latestVersion": "{{latest}}" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    [TestMethod]
    public void AnOutdatedPackageGetsItsNewestVersionAndTheProjectPathIsFull()
    {
        var listing = NuGetListParser.Combine(Installed(), Outdated("2.0.0"));

        Assert.IsTrue(listing.Succeeded);
        Assert.AreEqual(0, listing.Problems.Count);
        var usage = listing.Packages.Single();
        Assert.AreEqual(AppProject, usage.ProjectPath);
        Assert.AreEqual("Probe.Lib", usage.PackageId);
        Assert.AreEqual("1.0.0", usage.RequestedVersion);
        Assert.AreEqual("1.0.0", usage.ResolvedVersion);
        Assert.AreEqual("2.0.0", usage.LatestVersion);
    }

    [TestMethod]
    public void APackageThatIsUpToDateHasNoLatestVersion()
    {
        // dotnet lists only the outdated packages in the second listing: a package it does not list has no latest version.
        var listing = NuGetListParser.Combine(Installed(), """{ "version": 1, "projects": [] }""");

        Assert.IsNull(listing.Packages.Single().LatestVersion);
    }

    [TestMethod]
    public void TextInTheLatestFieldIsNotAVersion()
    {
        // When the sources do not hold a newer one, dotnet puts this text where the version goes.
        var listing = NuGetListParser.Combine(Installed(), Outdated("Not found at the sources"));

        Assert.IsNull(listing.Packages.Single().LatestVersion);
    }

    [TestMethod]
    public void TheSameProjectPathWithAnotherSpellingIsOneProject()
    {
        var listing = NuGetListParser.Combine(Installed("C:/src/App/App.csproj"), Outdated("2.0.0", "C:\\\\src\\\\App\\\\App.csproj"));

        var usage = listing.Packages.Single();
        Assert.AreEqual("2.0.0", usage.LatestVersion, "the latest version is matched to the package across the two spellings");
    }

    [TestMethod]
    public void AProjectWithTwoFrameworksListsEachPackageOnce()
    {
        const string json = """
            {
              "version": 1,
              "projects": [
                {
                  "path": "C:/src/App/App.csproj",
                  "frameworks": [
                    { "framework": "net8.0", "topLevelPackages": [ { "id": "Probe.Lib", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0" } ] },
                    { "framework": "net9.0", "topLevelPackages": [ { "id": "Probe.Lib", "requestedVersion": "1.0.0", "resolvedVersion": "1.0.0" } ] }
                  ]
                }
              ]
            }
            """;

        var listing = NuGetListParser.Combine(json, """{ "version": 1, "projects": [] }""");

        Assert.AreEqual(1, listing.Packages.Count);
    }

    [TestMethod]
    public void RestoreProblemsAreReportedAndNoPackagesAreListed()
    {
        const string json = """
            {
              "version": 1,
              "problems": [ { "text": "Restore failed. Run `dotnet restore` for more details on the issue.", "level": "error" } ]
            }
            """;

        var listing = NuGetListParser.Combine(json, null);

        Assert.IsFalse(listing.Succeeded);
        Assert.AreEqual(0, listing.Packages.Count);
        CollectionAssert.Contains(listing.Problems.ToList(), "Restore failed. Run `dotnet restore` for more details on the issue.");
    }

    [TestMethod]
    public void AListingThatIsNotJsonFails()
    {
        var listing = NuGetListParser.Combine("error: unknown command", null);

        Assert.IsFalse(listing.Succeeded);
        Assert.AreEqual(1, listing.Problems.Count(problem => problem.Length > 0));
    }

    [TestMethod]
    public void WhenTheOutdatedCheckCannotRunTheListingStillShowsTheInstalledPackages()
    {
        var listing = NuGetListParser.Combine(Installed(), null);

        Assert.IsTrue(listing.Succeeded);
        Assert.AreEqual(1, listing.Packages.Count);
        Assert.IsNull(listing.Packages.Single().LatestVersion);
        Assert.AreEqual(1, listing.Problems.Count, "the check that could not run is said");
    }

    [TestMethod]
    public void AnEmptySolutionSucceedsWithNoPackages()
    {
        var listing = NuGetListParser.Combine("""{ "version": 1, "projects": [] }""", """{ "version": 1, "projects": [] }""");

        Assert.IsTrue(listing.Succeeded);
        Assert.AreEqual(0, listing.Packages.Count);
    }

    [TestMethod]
    [DataRow("1.0.0", true)]
    [DataRow("13.0.3-beta.1", true)]
    [DataRow("Not found at the sources", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void OnlyTextThatStartsWithADigitIsAVersion(string? text, bool expected)
    {
        Assert.AreEqual(expected, NuGetListParser.IsVersion(text));
    }
}
