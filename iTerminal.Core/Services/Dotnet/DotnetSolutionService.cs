using iTerminal.Core.Model.Dotnet;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace iTerminal.Core.Services.Dotnet;

/// <summary>
/// <see cref="IDotnetSolutionService"/> backed by the <c>dotnet</c> CLI. Solution parsing is
/// pure file I/O; command execution streams process output line-by-line and kills the whole
/// process tree on cancellation so a long build/test/run can be stopped from the UI.
/// </summary>
public sealed partial class DotnetSolutionService : IDotnetSolutionService
{
    private static readonly string[] ProjectExtensions =
        [".csproj", ".vbproj", ".fsproj", ".dcproj"];

    // Legacy .sln line: Project("{type-guid}") = "Name", "relative\path.csproj", "{project-guid}"
    [GeneratedRegex("Project\\(\"\\{[^}]+\\}\"\\)\\s*=\\s*\"([^\"]*)\",\\s*\"([^\"]*)\"")]
    private static partial Regex LegacyProjectRegex();

    public SolutionInfo ParseSolution(string solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            return SolutionInfo.Empty;
        }

        var fullPath = Path.GetFullPath(solutionPath);
        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(fullPath);

        var relativePaths = solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnx(fullPath)
            : ParseLegacySln(fullPath);

        var projects = new List<SolutionProject>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in relativePaths)
        {
            var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                         .Replace('/', Path.DirectorySeparatorChar);
            var absolute = Path.GetFullPath(Path.Combine(directory, normalized));
            if (!seen.Add(absolute))
            {
                continue;
            }

            projects.Add(new SolutionProject(
                Path.GetFileNameWithoutExtension(normalized),
                relativePath,
                absolute,
                DetectIsTestProject(absolute)));
        }

        return new SolutionInfo(fullPath, name, directory, projects);
    }

    /// <summary>
    /// Reads the project file to decide whether it's a test project: the <c>IsTestProject</c>
    /// MSBuild property wins when present, otherwise falls back to a <c>Microsoft.NET.Test.Sdk</c>
    /// package reference (the property is often left unset in hand-written .csproj files).
    /// </summary>
    private static bool DetectIsTestProject(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return false;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch
        {
            return false;
        }

        var isTestProjectValue = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "IsTestProject")
            ?.Value;
        if (isTestProjectValue is not null && bool.TryParse(isTestProjectValue, out var isTestProject))
        {
            return isTestProject;
        }

        return document.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Any(element => string.Equals(
                element.Attribute("Include")?.Value,
                "Microsoft.NET.Test.Sdk",
                StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ParseSlnx(string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch
        {
            return [];
        }

        // <Project Path="..."/> elements, possibly nested under <Folder>. Namespace-agnostic.
        return document.Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(IsProjectPath)
            .Select(value => value!)
            .ToList();
    }

    private static IEnumerable<string> ParseLegacySln(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch
        {
            return [];
        }

        return LegacyProjectRegex().Matches(text)
            .Select(match => match.Groups[2].Value)
            .Where(IsProjectPath)
            .ToList();
    }

    private static bool IsProjectPath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && ProjectExtensions.Any(extension => value!.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public async Task<int> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };

        try
        {
            if (!process.Start())
            {
                onOutputLine("Failed to start dotnet.");
                return -1;
            }
        }
        catch (Exception exception)
        {
            onOutputLine($"Failed to start dotnet: {exception.Message}");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            onOutputLine("(cancelled)");
            return -1;
        }
    }
}
