using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class DevelopmentToolchainsTest
{
    private static readonly DevelopmentToolchains Toolchains = new();

    [Timeout(60_000)]
    [TestMethod]
    public void DotnetBuildTestCleanAndRunUseTheDotnetCli()
    {
        var project = new DevelopmentProject("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/App/App.csproj");
        var toolchain = Toolchains.For(project);

        CollectionAssert.AreEqual(new[] { "build", "/src/App/App.csproj" }, toolchain.Build(project)!.Arguments.ToArray());
        CollectionAssert.AreEqual(new[] { "test", "/src/App/App.csproj" }, toolchain.Test(project)!.Arguments.ToArray());
        CollectionAssert.AreEqual(new[] { "clean", "/src/App/App.csproj" }, toolchain.Clean(project)!.Arguments.ToArray());
        CollectionAssert.AreEqual(new[] { "run", "--project", "/src/App/App.csproj" }, toolchain.Run(project)!.Arguments.ToArray());
        Assert.AreEqual("/src/App", toolchain.Build(project)!.WorkingDirectory);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASolutionCannotBeRun()
    {
        var solution = new DevelopmentProject("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.Solution, "/src/App.slnx");

        Assert.IsNull(Toolchains.For(solution).Run(solution));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PythonRunsTestsWithPytestAndDebugsThroughDebugpy()
    {
        var script = new DevelopmentProject("tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, "/src/tool.py");
        var test = new DevelopmentProject("test_tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, "/src/test_tool.py", IsTest: true);
        var toolchain = Toolchains.For(script);

        CollectionAssert.AreEqual(new[] { "-m", "pytest", "-q", "/src/test_tool.py" }, toolchain.Test(test)!.Arguments.ToArray());
        CollectionAssert.AreEqual(new[] { "/src/tool.py" }, toolchain.Run(script)!.Arguments.ToArray());

        var plan = toolchain.PlanDebug(script, null, debugTests: false)!;
        CollectionAssert.AreEqual(new[] { "-m", "debugpy.adapter" }, plan.Arguments.ToArray());
        Assert.AreEqual("/src/tool.py", plan.Launch["program"]!.GetValue<string>());
        Assert.AreEqual("launch", plan.Launch["request"]!.GetValue<string>());

        var testPlan = toolchain.PlanDebug(test, null, debugTests: true)!;
        Assert.AreEqual("pytest", testPlan.Launch["module"]!.GetValue<string>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DotnetDebugLaunchesTheBuiltAssemblyWithNetcoredbg()
    {
        var project = new DevelopmentProject("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/App/App.csproj");

        var plan = Toolchains.For(project).PlanDebug(project, "/src/App/bin/Debug/net11.0/App.dll", debugTests: false)!;

        Assert.AreEqual("netcoredbg", plan.FileName);
        CollectionAssert.AreEqual(new[] { "--interpreter=vscode" }, plan.Arguments.ToArray());
        Assert.AreEqual("coreclr", plan.Launch["type"]!.GetValue<string>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PowerShellRunsAndTestsButOffersNoDebugger()
    {
        var script = new DevelopmentProject("tool.ps1", DevelopmentLanguage.PowerShell, DevelopmentProjectKind.Script, "/src/tool.ps1");
        var toolchain = Toolchains.For(script);

        CollectionAssert.AreEqual(new[] { "-NoProfile", "-File", "/src/tool.ps1" }, toolchain.Run(script)!.Arguments.ToArray());
        Assert.IsNull(toolchain.PlanDebug(script, null, debugTests: false));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void JavaBuildsAndTestsWithMavenAndRunsASourceFile()
    {
        var module = new DevelopmentProject("svc", DevelopmentLanguage.Java, DevelopmentProjectKind.JavaProject, "/src/svc/pom.xml");
        var file = new DevelopmentProject("Main.java", DevelopmentLanguage.Java, DevelopmentProjectKind.Script, "/src/Main.java");
        var toolchain = Toolchains.For(module);

        Assert.AreEqual("mvn", toolchain.Build(module)!.FileName);
        CollectionAssert.AreEqual(new[] { "-B", "test" }, toolchain.Test(module)!.Arguments.ToArray());
        CollectionAssert.AreEqual(new[] { "/src/Main.java" }, Toolchains.For(file).Run(file)!.Arguments.ToArray());
        Assert.AreEqual("javac", Toolchains.For(file).Build(file)!.FileName);
        Assert.IsNull(Toolchains.For(file).Test(file), "a single Java file has no test command");
        Assert.IsNull(toolchain.PlanDebug(module, null, debugTests: false), "Java debugging needs the java-debug adapter, which is not offered");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void JavaInAGradleModuleUsesTheWrapperWhenThereIsOne()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"JavaGradle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string wrapper = Path.Combine(folder, OperatingSystem.IsWindows() ? "gradlew.bat" : "gradlew");
            File.WriteAllText(wrapper, "#!/bin/sh\n");
            var module = new DevelopmentProject("app", DevelopmentLanguage.Java, DevelopmentProjectKind.JavaProject, Path.Combine(folder, "build.gradle"));

            Assert.AreEqual(wrapper, Toolchains.For(module).Test(module)!.FileName);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
