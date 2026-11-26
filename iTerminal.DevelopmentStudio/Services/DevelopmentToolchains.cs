using System.Text.Json.Nodes;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>The toolchain of each language, looked up by the item's language.</summary>
public sealed class DevelopmentToolchains
{
    private readonly Dictionary<DevelopmentLanguage, IDevelopmentToolchain> _byLanguage;

    public DevelopmentToolchains()
        : this([new DotNetToolchain(), new PythonToolchain(), new PowerShellToolchain(), new JavaToolchain()])
    {
    }

    public DevelopmentToolchains(IEnumerable<IDevelopmentToolchain> toolchains)
    {
        _byLanguage = toolchains.ToDictionary(t => t.Language);
    }

    public IDevelopmentToolchain For(DevelopmentLanguage language) => _byLanguage[language];

    public IDevelopmentToolchain For(DevelopmentProject project) => For(project.Language);
}

/// <summary>.NET: the <c>dotnet</c> CLI for build, run, clean and test; netcoredbg for debugging.</summary>
public sealed class DotNetToolchain : IDevelopmentToolchain
{
    public DevelopmentLanguage Language => DevelopmentLanguage.DotNet;

    /// <summary>The <c>dotnet</c> host, or the <c>DOTNET</c> host the user chose; the Development Studio uses the one on the PATH.</summary>
    public string Host { get; init; } = "dotnet";

    /// <summary>The debugger that speaks DAP: netcoredbg, on the PATH.</summary>
    public string Debugger { get; init; } = "netcoredbg";

    public ProcessCommand Build(DevelopmentProject project) => Dotnet(project, "build", project.Path);

    public ProcessCommand? Run(DevelopmentProject project) =>
        project.Kind == DevelopmentProjectKind.DotNetProject ? Dotnet(project, "run", "--project", project.Path) : null;

    public ProcessCommand Test(DevelopmentProject project) => Dotnet(project, "test", project.Path);

    public ProcessCommand Clean(DevelopmentProject project) => Dotnet(project, "clean", project.Path);

    public IReadOnlyList<string> CacheFolders(DevelopmentProject project) => [];

    /// <summary>The tests under coverlet's collector (<c>XPlat Code Coverage</c>), which writes a Cobertura report under the results folder.</summary>
    public IReadOnlyList<ProcessCommand> Coverage(DevelopmentProject project, string resultsFolder) =>
        [Dotnet(project, "test", project.Path, "--collect:XPlat Code Coverage", "--results-directory", resultsFolder)];

    public DebugPlan? PlanDebug(DevelopmentProject project, string? builtAssembly, bool debugTests)
    {
        if (project.Kind != DevelopmentProjectKind.DotNetProject || builtAssembly is null || debugTests) return null;
        return new DebugPlan(Debugger, ["--interpreter=vscode"], new JsonObject
        {
            ["type"] = "coreclr",
            ["request"] = "launch",
            ["program"] = builtAssembly,
            ["cwd"] = project.Directory,
            ["args"] = new JsonArray(),
            ["stopAtEntry"] = false,
            ["console"] = "internalConsole",
        });
    }

    public string? FindBuiltAssembly(DevelopmentProject project)
    {
        if (project.Kind != DevelopmentProjectKind.DotNetProject) return null;
        string debugFolder = Path.Combine(project.Directory, "bin", "Debug");
        if (!Directory.Exists(debugFolder)) return null;
        string assemblyName = project.Name + ".dll";
        try
        {
            return Directory.EnumerateFiles(debugFolder, assemblyName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private ProcessCommand Dotnet(DevelopmentProject project, params string[] arguments) =>
        new(Host, arguments, project.Directory);
}

/// <summary>Python: <c>python</c> runs scripts, <c>py_compile</c> is the build, <c>pytest</c> runs tests, debugpy debugs.</summary>
public sealed class PythonToolchain : IDevelopmentToolchain
{
    public DevelopmentLanguage Language => DevelopmentLanguage.Python;

    /// <summary>The interpreter: <c>python</c> on Windows, <c>python3</c> elsewhere.</summary>
    public string Interpreter { get; init; } = OperatingSystem.IsWindows() ? "python" : "python3";

    public ProcessCommand Build(DevelopmentProject project) => Python(project, "-m", "py_compile", project.Path);

    public ProcessCommand Run(DevelopmentProject project) => Python(project, project.Path);

    public ProcessCommand Test(DevelopmentProject project) => Python(project, "-m", "pytest", "-q", project.Path);

    public ProcessCommand? Clean(DevelopmentProject project) => null;

    public IReadOnlyList<string> CacheFolders(DevelopmentProject project)
    {
        var folders = new List<string>();
        foreach (string name in new[] { "__pycache__", ".pytest_cache" })
        {
            string top = Path.Combine(project.Directory, name);
            if (Directory.Exists(top)) folders.Add(top);
            foreach (string sub in SafeSubfolders(project.Directory))
            {
                string nested = Path.Combine(sub, name);
                if (Directory.Exists(nested)) folders.Add(nested);
            }
        }

        return folders;
    }

    /// <summary>coverage.py runs the test file (or the script) and <c>coverage xml</c> writes the Cobertura report.</summary>
    public IReadOnlyList<ProcessCommand> Coverage(DevelopmentProject project, string resultsFolder)
    {
        string data = Path.Combine(resultsFolder, ".coverage");
        string report = Path.Combine(resultsFolder, "coverage.xml");
        string[] target = project.IsTest ? ["-m", "pytest", "-q", project.Path] : [project.Path];
        return
        [
            Python(project, ["-m", "coverage", "run", "--data-file=" + data, .. target]),
            Python(project, ["-m", "coverage", "xml", "--data-file=" + data, "-o", report]),
        ];
    }

    /// <summary>cProfile writes the stats of the script to <paramref name="profilePath"/>.</summary>
    public ProcessCommand Profile(DevelopmentProject project, string profilePath) =>
        Python(project, ["-m", "cProfile", "-o", profilePath, project.Path]);

    public ProcessCommand ProfileSummary(DevelopmentProject project, string profilePath) =>
        Python(project, ["-c", iTerminal.DevelopmentStudio.Services.Analysis.PythonProfileParser.SummaryScript, profilePath]);

    public DebugPlan? PlanDebug(DevelopmentProject project, string? builtAssembly, bool debugTests)
    {
        if (project.Kind != DevelopmentProjectKind.Script || project.Language != DevelopmentLanguage.Python) return null;
        // debugpy runs a module as well as a file: a test file goes through pytest, so its asserts stop the debugger too.
        var launch = new JsonObject
        {
            ["name"] = project.Name,
            ["type"] = "python",
            ["request"] = "launch",
            ["cwd"] = project.Directory,
            ["console"] = "internalConsole",
            ["justMyCode"] = false,
            ["stopOnEntry"] = false,
        };
        if (debugTests)
        {
            launch["module"] = "pytest";
            launch["args"] = new JsonArray("-q", project.Path);
        }
        else
        {
            launch["program"] = project.Path;
            launch["args"] = new JsonArray();
        }

        return new DebugPlan(Interpreter, ["-m", "debugpy.adapter"], launch);
    }

    private ProcessCommand Python(DevelopmentProject project, params string[] arguments) =>
        new(Interpreter, arguments, project.Directory);

    private static IEnumerable<string> SafeSubfolders(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(sub => !Path.GetFileName(sub).StartsWith('.') && Path.GetFileName(sub) is not ("venv" or "env" or "node_modules" or "bin" or "obj"))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>PowerShell: <c>pwsh</c> runs scripts, the Parser checks the syntax, Pester runs the tests. No debugger is offered.</summary>
public sealed class PowerShellToolchain : IDevelopmentToolchain
{
    public DevelopmentLanguage Language => DevelopmentLanguage.PowerShell;

    public string Shell { get; init; } = "pwsh";

    public ProcessCommand Build(DevelopmentProject project)
    {
        string path = QuoteForPowerShell(project.Path);
        string script = "$t=$null;$e=$null;" +
            $"[System.Management.Automation.Language.Parser]::ParseFile('{path}',[ref]$t,[ref]$e)|Out-Null;" +
            "if($e){$e|ForEach-Object{$_.Message};exit 1}else{'Syntax check passed.'}";
        return Pwsh(project, "-Command", script);
    }

    public ProcessCommand Run(DevelopmentProject project) => Pwsh(project, "-File", project.Path);

    public ProcessCommand Test(DevelopmentProject project) =>
        Pwsh(project, "-Command", $"Invoke-Pester -Path '{QuoteForPowerShell(project.Path)}'");

    public ProcessCommand? Clean(DevelopmentProject project) => null;

    public IReadOnlyList<string> CacheFolders(DevelopmentProject project) => [];

    public DebugPlan? PlanDebug(DevelopmentProject project, string? builtAssembly, bool debugTests) => null;

    private ProcessCommand Pwsh(DevelopmentProject project, params string[] arguments) =>
        new(Shell, ["-NoProfile", .. arguments], project.Directory);

    /// <summary>Doubles the single quotes of a path, so it can sit inside a quoted PowerShell string.</summary>
    public static string QuoteForPowerShell(string path) => path.Replace("'", "''");
}

/// <summary>
/// Java: Maven or Gradle for project folders (<c>pom.xml</c>, <c>build.gradle</c>), <c>javac</c> and the source launcher
/// (<c>java File.java</c>) for single files. No debugger is offered: the Eclipse java-debug adapter needs a jar the
/// Development Studio does not ship, so debugging Java says so rather than failing silently.
/// </summary>
public sealed class JavaToolchain : IDevelopmentToolchain
{
    public DevelopmentLanguage Language => DevelopmentLanguage.Java;

    public string Maven { get; init; } = "mvn";

    public string Gradle { get; init; } = "gradle";

    public string Javac { get; init; } = "javac";

    public string JavaRuntime { get; init; } = "java";

    public ProcessCommand Build(DevelopmentProject project)
    {
        if (project.Kind == DevelopmentProjectKind.Script) return new ProcessCommand(Javac, [project.Path], project.Directory);
        return project.Path.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase)
            ? new ProcessCommand(Maven, ["-B", "-q", "compile"], project.Directory)
            : new ProcessCommand(GradleHost(project), ["build", "-x", "test"], project.Directory);
    }

    public ProcessCommand? Run(DevelopmentProject project)
    {
        if (project.Kind == DevelopmentProjectKind.Script) return new ProcessCommand(JavaRuntime, [project.Path], project.Directory);
        return project.Path.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase)
            ? new ProcessCommand(Maven, ["-B", "-q", "exec:java"], project.Directory)
            : new ProcessCommand(GradleHost(project), ["run"], project.Directory);
    }

    public ProcessCommand? Test(DevelopmentProject project)
    {
        if (project.Kind == DevelopmentProjectKind.Script) return null;
        return project.Path.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase)
            ? new ProcessCommand(Maven, ["-B", "test"], project.Directory)
            : new ProcessCommand(GradleHost(project), ["test"], project.Directory);
    }

    public ProcessCommand? Clean(DevelopmentProject project)
    {
        if (project.Kind == DevelopmentProjectKind.Script) return null;
        return project.Path.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase)
            ? new ProcessCommand(Maven, ["-B", "clean"], project.Directory)
            : new ProcessCommand(GradleHost(project), ["clean"], project.Directory);
    }

    public IReadOnlyList<string> CacheFolders(DevelopmentProject project) => [];

    public DebugPlan? PlanDebug(DevelopmentProject project, string? builtAssembly, bool debugTests) => null;

    /// <summary>The project's Gradle wrapper when it has one (the usual setup), otherwise the <c>gradle</c> on the PATH.</summary>
    private string GradleHost(DevelopmentProject project)
    {
        string wrapper = Path.Combine(project.Directory, OperatingSystem.IsWindows() ? "gradlew.bat" : "gradlew");
        return File.Exists(wrapper) ? wrapper : Gradle;
    }
}
