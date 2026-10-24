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

    public IReadOnlyList<DotnetSdk> ListSdks()
    {
        try
        {
            using var process = Start("dotnet", ["--list-sdks"], out _);
            if (process is null)
            {
                return [];
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(TimeSpan.FromSeconds(30));
            return DotnetSdk.ParseListing(output.Split('\n'));
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>How long the output may still arrive after the process has exited.</summary>
    private static readonly TimeSpan OutputGracePeriod = TimeSpan.FromSeconds(2);

    public async Task<int> RunAsync(
        string workingDirectory,
        string dotnetPath,
        IReadOnlyList<string> arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = Start(dotnetPath, arguments, out string? startError, workingDirectory, onOutputLine, exited);
        if (process is null)
        {
            onOutputLine(startError ?? "Failed to start dotnet.");
            return -1;
        }

        using var registration = cancellationToken.Register(() =>
        {
            // Cancel before killing: killing raises the exit event at once, and that would finish the run as an exit code.
            exited.TrySetCanceled(cancellationToken);
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        try
        {
            int exitCode = await exited.Task;

            // The process has exited, but a child it left running (MSBuild keeps a build node alive) can hold the
            // output open. Wait for the last lines a short while, not for the child to finish.
            await Task.WhenAny(Task.Run(() => process.WaitForExit()), Task.Delay(OutputGracePeriod));
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            onOutputLine("(cancelled)");
            return -1;
        }
    }

    /// <summary>
    /// Starts a process with redirected output. With <paramref name="onOutputLine"/> it reports each line and the exit to
    /// <paramref name="exited"/>; without, the caller reads the standard output itself.
    /// </summary>
    private static Process? Start(
        string fileName,
        IReadOnlyList<string> arguments,
        out string? error,
        string? workingDirectory = null,
        Action<string>? onOutputLine = null,
        TaskCompletionSource<int>? exited = null)
    {
        error = null;
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = exited is not null };
        if (exited is not null)
        {
            process.Exited += (_, _) => exited.TrySetResult(process.ExitCode);
        }

        if (onOutputLine is not null)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
        }

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return null;
            }
        }
        catch (Exception exception)
        {
            process.Dispose();
            error = $"Failed to start dotnet: {exception.Message}";
            return null;
        }

        if (onOutputLine is not null)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        return process;
    }
}
