using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;
using Moq;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class DevelopmentWorkspaceScannerTest
{
    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"Scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FindsSolutionsProjectsScriptsAndJavaModulesAndSkipsCaches()
    {
        string folder = NewFolder();
        try
        {
            string app = Path.Combine(folder, "App", "App.csproj");
            string test = Path.Combine(folder, "App.Test", "App.Test.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(app)!);
            Directory.CreateDirectory(Path.GetDirectoryName(test)!);
            File.WriteAllText(app, "<Project />");
            File.WriteAllText(test, "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(folder, "App.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(folder, "tool.py"), "print(1)");
            File.WriteAllText(Path.Combine(folder, "test_tool.py"), "");
            File.WriteAllText(Path.Combine(folder, "Deploy.ps1"), "");
            File.WriteAllText(Path.Combine(folder, "Deploy.Tests.ps1"), "");
            string module = Path.Combine(folder, "svc");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "pom.xml"), "<project/>");
            File.WriteAllText(Path.Combine(module, "Main.java"), "class Main {}");
            string cache = Path.Combine(folder, "__pycache__");
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "ignored.py"), "");

            var dotnet = new Mock<IDotnetSolutionService>();
            dotnet.Setup(s => s.ParseSolution(It.IsAny<string>())).Returns(new SolutionInfo(
                Path.Combine(folder, "App.slnx"), "App", folder,
                [
                    new SolutionProject("App", "App/App.csproj", app),
                    new SolutionProject("App.Test", "App.Test/App.Test.csproj", test, IsTestProject: true),
                ]));

            var items = new DevelopmentWorkspaceScanner(dotnet.Object).Scan(folder);

            var summary = items.Select(i => (i.Kind, i.Language, i.Name, i.IsTest)).ToList();
            CollectionAssert.Contains(summary, (DevelopmentProjectKind.Solution, DevelopmentLanguage.DotNet, "App", false));
            CollectionAssert.Contains(summary, (DevelopmentProjectKind.DotNetProject, DevelopmentLanguage.DotNet, "App.Test", true));
            CollectionAssert.Contains(summary, (DevelopmentProjectKind.Script, DevelopmentLanguage.Python, "test_tool.py", true));
            CollectionAssert.Contains(summary, (DevelopmentProjectKind.Script, DevelopmentLanguage.PowerShell, "Deploy.Tests.ps1", true));
            CollectionAssert.Contains(summary, (DevelopmentProjectKind.JavaProject, DevelopmentLanguage.Java, "svc", false));
            Assert.AreEqual(Path.Combine(folder, "App.slnx"), items.Single(i => i.Name == "App" && i.Kind == DevelopmentProjectKind.DotNetProject).SolutionPath,
                "a project the solution lists names that solution, so the explorer draws it under it");
            Assert.IsNull(items.Single(i => i.Kind == DevelopmentProjectKind.Solution).SolutionPath);
            Assert.IsFalse(items.Any(i => i.Name == "ignored.py"), "__pycache__ is skipped");
            Assert.IsFalse(items.Any(i => i.Name == "Main.java"), "a .java file inside a Java module is shown under the module");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsWhetherAProjectIsAConsoleWebOrLibraryProject()
    {
        Assert.AreEqual(DotNetOutputKind.Web, DevelopmentWorkspaceScanner.OutputOf("<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup /></Project>"));
        Assert.AreEqual(DotNetOutputKind.Console, DevelopmentWorkspaceScanner.OutputOf("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>"));
        Assert.AreEqual(DotNetOutputKind.Console, DevelopmentWorkspaceScanner.OutputOf("<Project Sdk=\"Microsoft.NET.Sdk.Worker\"></Project>"));
        Assert.AreEqual(DotNetOutputKind.Library, DevelopmentWorkspaceScanner.OutputOf("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>"));
        Assert.AreEqual(DotNetOutputKind.Library, DevelopmentWorkspaceScanner.OutputOf("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Library</OutputType></PropertyGroup></Project>"));
        Assert.AreEqual(DotNetOutputKind.Unknown, DevelopmentWorkspaceScanner.OutputOf("<Project />"), "a file without an SDK is not judged");
        Assert.AreEqual(DotNetOutputKind.Unknown, DevelopmentWorkspaceScanner.OutputOf(string.Empty));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFolderThatDoesNotExistHasNoItems()
    {
        var dotnet = new Mock<IDotnetSolutionService>();

        Assert.AreEqual(0, new DevelopmentWorkspaceScanner(dotnet.Object).Scan(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Count);
    }
}
