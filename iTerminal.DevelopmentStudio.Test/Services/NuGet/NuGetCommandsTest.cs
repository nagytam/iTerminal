using iTerminal.DevelopmentStudio.Services.NuGet;

namespace iTerminal.DevelopmentStudio.Test.Services.NuGet;

[TestClass]
public class NuGetCommandsTest
{
    [TestMethod]
    public void AddWithoutAVersionAddsTheNewestOne()
    {
        CollectionAssert.AreEqual(new[] { "add", "App.csproj", "package", "Newtonsoft.Json" }, NuGetCommands.AddArguments("App.csproj", "Newtonsoft.Json", null).ToArray());
    }

    [TestMethod]
    public void AddWithAVersionPinsIt()
    {
        CollectionAssert.AreEqual(new[] { "add", "App.csproj", "package", "Newtonsoft.Json", "--version", "13.0.3" }, NuGetCommands.AddArguments("App.csproj", "Newtonsoft.Json", "13.0.3").ToArray());
    }

    [TestMethod]
    public void RemoveNamesTheProjectAndThePackage()
    {
        CollectionAssert.AreEqual(new[] { "remove", "App.csproj", "package", "Serilog" }, NuGetCommands.RemoveArguments("App.csproj", "Serilog").ToArray());
    }

    [TestMethod]
    public void ListAsksForTheJsonOfTheInstalledOrTheOutdatedPackages()
    {
        CollectionAssert.AreEqual(new[] { "list", "App.slnx", "package", "--format", "json" }, NuGetCommands.ListArguments("App.slnx", outdated: false).ToArray());
        CollectionAssert.AreEqual(new[] { "list", "App.slnx", "package", "--outdated", "--format", "json" }, NuGetCommands.ListArguments("App.slnx", outdated: true).ToArray());
    }

    [TestMethod]
    [DataRow("Newtonsoft.Json", true)]
    [DataRow("System.Text.Json", true)]
    [DataRow("Probe_Lib-2", true)]
    [DataRow("9patch", true)]
    [DataRow("-rf", false)]
    [DataRow(".hidden", false)]
    [DataRow("has space", false)]
    [DataRow("semi;colon", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void APackageIdIsWhatNuGetAccepts(string? id, bool expected)
    {
        Assert.AreEqual(expected, NuGetCommands.IsPackageId(id));
    }

    [TestMethod]
    [DataRow("13.0.3", true)]
    [DataRow("2.0.0-rc.1+build", true)]
    [DataRow("-1", false)]
    [DataRow("1.0 beta", false)]
    [DataRow("latest", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void AVersionIsADigitFollowedByVersionCharacters(string? version, bool expected)
    {
        Assert.AreEqual(expected, NuGetCommands.IsVersionText(version));
    }
}
