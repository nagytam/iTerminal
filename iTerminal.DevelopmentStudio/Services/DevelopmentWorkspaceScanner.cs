using System.Text.RegularExpressions;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.Core.Services.Dotnet;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>Scans a folder tree (bounded in depth and in file count) for the items the Development Studio can run.</summary>
public sealed partial class DevelopmentWorkspaceScanner(IDotnetSolutionService dotnetSolutionService) : IDevelopmentWorkspaceScanner
{
    /// <summary>How many folder levels below the chosen folder are searched.</summary>
    public const int MaxDepth = 5;

    /// <summary>The scan stops adding items past this count, so a folder of thousands of files stays quick.</summary>
    public const int MaxItems = 2000;

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".vscode", ".idea", "bin", "obj", "node_modules", ".venv", "venv", "env", "__pycache__",
        ".pytest_cache", "TestResults", "packages", ".mypy_cache",
    };

    [GeneratedRegex(@"<IsTestProject>\s*true\s*</IsTestProject>", RegexOptions.IgnoreCase)]
    private static partial Regex IsTestProjectRegex();

    [GeneratedRegex(@"<OutputType>\s*(Exe|WinExe)\s*</OutputType>", RegexOptions.IgnoreCase)]
    private static partial Regex ExecutableOutputRegex();

    [GeneratedRegex(@"<OutputType>\s*Library\s*</OutputType>", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryOutputRegex();

    [GeneratedRegex(@"<Project\s[^>]*Sdk\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectSdkRegex();

    /// <inheritdoc />
    public IReadOnlyList<DevelopmentProject> Scan(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        var files = new List<string>();
        CollectFiles(Path.GetFullPath(folder), 0, files);
        files.Sort(StringComparer.OrdinalIgnoreCase);

        var items = new List<DevelopmentProject>();
        var claimedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in files.Where(f => IsSolution(f)))
        {
            if (items.Count >= MaxItems) break;
            items.Add(new DevelopmentProject(Path.GetFileNameWithoutExtension(file), DevelopmentLanguage.DotNet, DevelopmentProjectKind.Solution, file));
            foreach (var project in ProjectsOf(file))
            {
                if (items.Count >= MaxItems) break;
                claimedProjects.Add(project.Path);
                items.Add(project);
            }
        }

        // A Java module is a folder with a build file; the .java files inside it are shown under the project, not again.
        var javaProjectFolders = new List<string>();
        foreach (string file in files.Where(IsJavaBuildFile))
        {
            if (items.Count >= MaxItems) break;
            string moduleFolder = Path.GetDirectoryName(file) ?? string.Empty;
            javaProjectFolders.Add(moduleFolder);
            items.Add(new DevelopmentProject(Path.GetFileName(moduleFolder) is { Length: > 0 } name ? name : Path.GetFileName(file), DevelopmentLanguage.Java, DevelopmentProjectKind.JavaProject, file, IsJavaTestFolder(moduleFolder)));
        }

        foreach (string file in files)
        {
            if (items.Count >= MaxItems) break;
            string extension = Path.GetExtension(file);
            if (IsDotNetProjectFile(extension))
            {
                if (claimedProjects.Contains(file)) continue;
                string text = SafeRead(file);
                bool isTest = IsTestProjectRegex().IsMatch(text);
                items.Add(new DevelopmentProject(Path.GetFileNameWithoutExtension(file), DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, file, isTest, null, OutputOf(text)));
            }
            else if (extension.Equals(".py", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new DevelopmentProject(Path.GetFileName(file), DevelopmentLanguage.Python, DevelopmentProjectKind.Script, file, IsPythonTest(file)));
            }
            else if (extension.Equals(".java", StringComparison.OrdinalIgnoreCase))
            {
                string sourceFolder = Path.GetDirectoryName(file) ?? string.Empty;
                if (javaProjectFolders.Any(project => file.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                items.Add(new DevelopmentProject(Path.GetFileName(file), DevelopmentLanguage.Java, DevelopmentProjectKind.Script, file, file.EndsWith("Test.java", StringComparison.Ordinal) || IsJavaTestFolder(sourceFolder)));
            }
            else if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new DevelopmentProject(Path.GetFileName(file), DevelopmentLanguage.PowerShell, DevelopmentProjectKind.Script, file, file.EndsWith(".Tests.ps1", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return items;
    }

    /// <summary>The projects a solution lists, as items. A project file that is missing from disk is left out.</summary>
    private IEnumerable<DevelopmentProject> ProjectsOf(string solutionPath)
    {
        iTerminal.Core.Model.Dotnet.SolutionInfo solution;
        try
        {
            solution = dotnetSolutionService.ParseSolution(solutionPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or FormatException)
        {
            // A solution that does not parse still shows as a solution row; it simply has no projects in the explorer.
            return [];
        }

        return solution.Projects
            .Where(project => File.Exists(project.AbsolutePath))
            .Select(project => new DevelopmentProject(project.Name, DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, project.AbsolutePath, project.IsTestProject, solutionPath, OutputOf(SafeRead(project.AbsolutePath))))
            .ToList();
    }

    private void CollectFiles(string folder, int depth, List<string> files)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (files.Count >= MaxItems * 4) return;
                files.Add(file);
            }

            if (depth >= MaxDepth) return;
            foreach (string sub in Directory.EnumerateDirectories(folder))
            {
                if (SkippedFolders.Contains(Path.GetFileName(sub))) continue;
                CollectFiles(sub, depth + 1, files);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder we cannot read is skipped; the rest of the tree is still scanned.
        }
    }

    private static bool IsJavaBuildFile(string file)
    {
        string name = Path.GetFileName(file);
        return name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A Java folder under a <c>test</c> folder (the Maven and Gradle layout) holds tests.</summary>
    private static bool IsJavaTestFolder(string folder) =>
        folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part.Equals("test", StringComparison.OrdinalIgnoreCase));

    private static bool IsSolution(string file) =>
        file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    private static bool IsDotNetProjectFile(string extension) =>
        extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a project builds to, from the text of its project file: the web SDK is a web project, an <c>Exe</c> output or the
    /// worker SDK is a console project, a <c>Library</c> output or the plain SDK without an output type is a library.
    /// </summary>
    public static DotNetOutputKind OutputOf(string projectFileText)
    {
        if (string.IsNullOrWhiteSpace(projectFileText)) return DotNetOutputKind.Unknown;
        string sdk = ProjectSdkRegex().Match(projectFileText) is { Success: true } match ? match.Groups[1].Value : string.Empty;
        if (sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)) return DotNetOutputKind.Web;
        if (ExecutableOutputRegex().IsMatch(projectFileText) || sdk.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase)) return DotNetOutputKind.Console;
        if (LibraryOutputRegex().IsMatch(projectFileText)) return DotNetOutputKind.Library;
        // A file that holds a project element with no output type is a library under the plain SDK; anything else is left unknown.
        return sdk.Equals("Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase) && projectFileText.Contains("</Project>", StringComparison.OrdinalIgnoreCase)
            ? DotNetOutputKind.Library
            : DotNetOutputKind.Unknown;
    }

    /// <summary>A Python test file by the usual names (<c>test_*.py</c>, <c>*_test.py</c>) or a file under a <c>tests</c> folder.</summary>
    public static bool IsPythonTest(string file)
    {
        string name = Path.GetFileName(file);
        if (name.StartsWith("test_", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_test.py", StringComparison.OrdinalIgnoreCase)) return true;
        string folder = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
        return folder.Equals("tests", StringComparison.OrdinalIgnoreCase) || folder.Equals("test", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeRead(string file)
    {
        try
        {
            // A project file is small. It is read whole, so OutputType is found wherever the file puts it.
            return File.ReadAllText(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
