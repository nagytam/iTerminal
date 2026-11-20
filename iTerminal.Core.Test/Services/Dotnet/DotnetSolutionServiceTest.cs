using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using System.Xml.Linq;

namespace iTerminal.Core.Test.Services.Dotnet;

[TestClass]
public class DotnetSolutionServiceTest
{
    private static string CreateTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotnetsvc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static bool DotnetAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process!.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ── GenerateSbom ─────────────────────────────────────────────────────────

    private const string AssetsWithTwoPackages =
        """
        {
          "version": 3,
          "libraries": {
            "Newtonsoft.Json/13.0.3": { "type": "package" },
            "Serilog/4.2.0+build.1": { "type": "package" },
            "App.Core/1.0.0": { "type": "project" }
          }
        }
        """;

    private static void WriteAssets(string projectDirectory, string json)
    {
        var obj = Path.Combine(projectDirectory, "obj");
        Directory.CreateDirectory(obj);
        File.WriteAllText(Path.Combine(obj, "project.assets.json"), json);
    }

    private static SolutionInfo SolutionWith(string root, params string[] projectNames)
    {
        var projects = projectNames
            .Select(name => new SolutionProject(name, name + "/" + name + ".csproj", Path.Combine(root, name, name + ".csproj"), false))
            .ToList();
        return new SolutionInfo(Path.Combine(root, "Demo.slnx"), "Demo", root, projects);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GenerateSbom_Xml_ListsPackagesOnceAcrossProjectsAndWritesBesideTheSolution()
    {
        var root = CreateTempDir();
        WriteAssets(Path.Combine(root, "App"), AssetsWithTwoPackages);
        WriteAssets(Path.Combine(root, "Core"), """
            { "libraries": { "newtonsoft.json/13.0.3": { "type": "package" } } }
            """);

        var result = new DotnetSolutionService().GenerateSbom(SolutionWith(root, "App", "Core"), SbomFormat.Xml);

        Assert.AreEqual(Path.Combine(root, "Demo.cdx.xml"), result.Path);
        Assert.AreEqual(2, result.ComponentCount);
        Assert.AreEqual(0, result.ProjectsWithoutAssets);

        var document = System.Xml.Linq.XDocument.Load(result.Path);
        XNamespace ns = "http://cyclonedx.org/schema/bom/1.6";
        Assert.AreEqual(ns + "bom", document.Root!.Name);
        var purls = document.Descendants(ns + "purl").Select(element => element.Value).ToList();
        CollectionAssert.AreEqual(
            new[] { "pkg:nuget/newtonsoft.json@13.0.3", "pkg:nuget/serilog@4.2.0%2Bbuild.1" },
            purls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GenerateSbom_Json_WritesCycloneDxJsonAndNoProjectReferences()
    {
        var root = CreateTempDir();
        WriteAssets(Path.Combine(root, "App"), AssetsWithTwoPackages);

        var result = new DotnetSolutionService().GenerateSbom(SolutionWith(root, "App"), SbomFormat.Json);

        Assert.AreEqual(Path.Combine(root, "Demo.cdx.json"), result.Path);
        Assert.AreEqual(2, result.ComponentCount);

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(result.Path));
        var bom = document.RootElement;
        Assert.AreEqual("CycloneDX", bom.GetProperty("bomFormat").GetString());
        Assert.AreEqual("1.6", bom.GetProperty("specVersion").GetString());
        Assert.AreEqual("Demo", bom.GetProperty("metadata").GetProperty("component").GetProperty("name").GetString());
        Assert.AreEqual(2, bom.GetProperty("components").GetArrayLength());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GenerateSbom_ProjectWithoutAssets_IsCountedAndSkipped()
    {
        var root = CreateTempDir();
        WriteAssets(Path.Combine(root, "App"), AssetsWithTwoPackages);

        var result = new DotnetSolutionService().GenerateSbom(SolutionWith(root, "App", "NotRestored"), SbomFormat.Xml);

        Assert.AreEqual(1, result.ProjectsWithoutAssets);
        Assert.AreEqual(2, result.ComponentCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GenerateSbom_NoProjectRestored_WritesNothing()
    {
        var root = CreateTempDir();

        var result = new DotnetSolutionService().GenerateSbom(SolutionWith(root, "App"), SbomFormat.Xml);

        Assert.AreEqual(string.Empty, result.Path);
        Assert.AreEqual(1, result.ProjectsWithoutAssets);
        Assert.IsFalse(File.Exists(Path.Combine(root, "Demo.cdx.xml")));
    }

    // ── ParseSolution: slnx ──────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void ParseSolution_Slnx_ReturnsProjectsWithAbsolutePaths()
    {
        var root = CreateTempDir();
        var slnxPath = Path.Combine(root, "Demo.slnx");
        File.WriteAllText(slnxPath,
            """
            <Solution>
              <Folder Name="/Solution Items/">
                <File Path=".editorconfig" />
              </Folder>
              <Project Path="Core/Core.csproj" />
              <Project Path="App\App.csproj">
                <BuildDependency Project="Core/Core.csproj" />
              </Project>
            </Solution>
            """);

        var service = new DotnetSolutionService();
        var solution = service.ParseSolution(slnxPath);

        Assert.AreEqual("Demo", solution.Name);
        Assert.AreEqual(Path.GetFullPath(root), solution.Directory.TrimEnd(Path.DirectorySeparatorChar));
        Assert.AreEqual(2, solution.Projects.Count);
        Assert.AreEqual("Core", solution.Projects[0].Name);
        Assert.AreEqual("App", solution.Projects[1].Name);
        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(root, "Core", "Core.csproj")),
            solution.Projects[0].AbsolutePath);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseSolution_Slnx_IgnoresNonProjectFilesAndDuplicates()
    {
        var root = CreateTempDir();
        var slnxPath = Path.Combine(root, "Dup.slnx");
        File.WriteAllText(slnxPath,
            """
            <Solution>
              <Project Path="A/A.csproj" />
              <Project Path="A/A.csproj" />
              <Project Path="docker-compose.dcproj" />
            </Solution>
            """);

        var service = new DotnetSolutionService();
        var solution = service.ParseSolution(slnxPath);

        // Duplicate A.csproj collapses to one; the .dcproj is a recognised project type.
        Assert.AreEqual(2, solution.Projects.Count);
        CollectionAssert.AreEquivalent(
            new[] { "A", "docker-compose" },
            solution.Projects.Select(p => p.Name).ToArray());
    }

    // ── ParseSolution: IsTestProject detection ───────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void ParseSolution_ProjectDeclaresIsTestProjectTrue_IsTestProjectIsTrue()
    {
        var root = CreateTempDir();
        Directory.CreateDirectory(Path.Combine(root, "Core.Test"));
        File.WriteAllText(Path.Combine(root, "Core.Test", "Core.Test.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);
        var slnxPath = Path.Combine(root, "Demo.slnx");
        File.WriteAllText(slnxPath, """<Solution><Project Path="Core.Test/Core.Test.csproj" /></Solution>""");

        var service = new DotnetSolutionService();
        var solution = service.ParseSolution(slnxPath);

        Assert.IsTrue(solution.Projects[0].IsTestProject);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseSolution_ProjectReferencesTestSdkWithoutIsTestProjectProperty_IsTestProjectIsTrue()
    {
        var root = CreateTempDir();
        Directory.CreateDirectory(Path.Combine(root, "Core.Test"));
        File.WriteAllText(Path.Combine(root, "Core.Test", "Core.Test.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
              </ItemGroup>
            </Project>
            """);
        var slnxPath = Path.Combine(root, "Demo.slnx");
        File.WriteAllText(slnxPath, """<Solution><Project Path="Core.Test/Core.Test.csproj" /></Solution>""");

        var service = new DotnetSolutionService();
        var solution = service.ParseSolution(slnxPath);

        Assert.IsTrue(solution.Projects[0].IsTestProject);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseSolution_OrdinaryProject_IsTestProjectIsFalse()
    {
        var root = CreateTempDir();
        Directory.CreateDirectory(Path.Combine(root, "Core"));
        File.WriteAllText(Path.Combine(root, "Core", "Core.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var slnxPath = Path.Combine(root, "Demo.slnx");
        File.WriteAllText(slnxPath, """<Solution><Project Path="Core/Core.csproj" /></Solution>""");

        var service = new DotnetSolutionService();
        var solution = service.ParseSolution(slnxPath);

        Assert.IsFalse(solution.Projects[0].IsTestProject);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseSolution_ProjectDeclaresIsTestProjectFalse_OverridesTestSdkReference_IsTestProjectIsFalse()
    {
        var root = CreateTempDir();
        Directory.CreateDirectory(Path.Combine(root, "Fixture"));
        File.WriteAllText(Path.Combine(root, "Fixture", "Fixture.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <IsTestProject>false</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
              </ItemGroup>
            </Project>
            """);
        var slnxPath = Path.Combine(root, "Demo.slnx");
        File.WriteAllText(slnxPath, """<Solution><Project Path="Fixture/Fixture.csproj" /></Solution>""");

        var service = new DotnetSolutionService();
        var solution = service.ParseSolution(slnxPath);

        Assert.IsFalse(solution.Projects[0].IsTestProject);
    }

    // ── ParseSolution: legacy .sln ───────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void ParseSolution_LegacySln_ReturnsProjectsSkippingSolutionFolders()
    {
        var root = CreateTempDir();
        var slnPath = Path.Combine(root, "Legacy.sln");
        File.WriteAllText(slnPath,
            "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Lib\", \"Lib\\Lib.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\n" +
            "EndProject\r\n" +
            "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"SolutionItems\", \"SolutionItems\", \"{22222222-2222-2222-2222-222222222222}\"\r\n" +
            "EndProject\r\n");

        var service = new DotnetSolutionService();
        var solution = service.ParseSolution(slnPath);

        // The solution folder (path == name, no project extension) is skipped.
        Assert.AreEqual(1, solution.Projects.Count);
        Assert.AreEqual("Lib", solution.Projects[0].Name);
    }

    // ── ParseSolution: error handling ────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void ParseSolution_MissingFile_ReturnsEmpty()
    {
        var service = new DotnetSolutionService();
        var solution = service.ParseSolution(Path.Combine(CreateTempDir(), "nope.slnx"));

        Assert.AreEqual(0, solution.Projects.Count);
        Assert.AreEqual(string.Empty, solution.Path);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseSolution_MalformedSlnx_ReturnsNoProjects()
    {
        var root = CreateTempDir();
        var slnxPath = Path.Combine(root, "Bad.slnx");
        File.WriteAllText(slnxPath, "<Solution><Project Path=");

        var service = new DotnetSolutionService();
        var solution = service.ParseSolution(slnxPath);

        Assert.AreEqual(0, solution.Projects.Count);
        // Still reports the file path/name even though parsing yielded nothing.
        Assert.AreEqual("Bad", solution.Name);
    }

    // ── RunAsync ─────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task RunAsync_StreamsOutput_AndReturnsExitCode()
    {
        if (!DotnetAvailable())
        {
            Assert.Inconclusive("dotnet CLI not available.");
            return;
        }

        var service = new DotnetSolutionService();
        var lines = new List<string>();

        var exitCode = await service.RunAsync(
            CreateTempDir(),
            "dotnet",
            ["--version"],
            line => { lock (lines) lines.Add(line); });

        Assert.AreEqual(0, exitCode);
        Assert.IsTrue(lines.Count > 0, "Expected dotnet --version to produce output.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RunAsync_WhenCancelled_ReturnsNegative()
    {
        if (!DotnetAvailable())
        {
            Assert.Inconclusive("dotnet CLI not available.");
            return;
        }

        var service = new DotnetSolutionService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exitCode = await service.RunAsync(
            CreateTempDir(),
            "dotnet",
            ["--version"],
            _ => { },
            cts.Token);

        Assert.IsTrue(exitCode < 0);
    }
}
