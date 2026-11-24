using iTerminal.Core.Model.Dotnet;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
        && ProjectExtensions.Any(extension => value.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public SbomResult GenerateSbom(SolutionInfo solution, SbomFormat format)
    {
        if (string.IsNullOrEmpty(solution.Directory))
        {
            return new SbomResult(string.Empty, 0, 0);
        }

        var components = new Dictionary<string, SbomComponent>(StringComparer.OrdinalIgnoreCase);
        var withAssets = 0;
        var withoutAssets = 0;
        foreach (var project in solution.Projects)
        {
            var projectDirectory = Path.GetDirectoryName(project.AbsolutePath) ?? string.Empty;
            var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
            if (!TryReadPackages(assetsPath, out var packages))
            {
                withoutAssets++;
                continue;
            }

            withAssets++;
            foreach (var package in packages)
            {
                components.TryAdd(package.Purl, package);
            }
        }

        if (withAssets == 0)
        {
            return new SbomResult(string.Empty, 0, withoutAssets);
        }

        var sorted = components.Values
            .OrderBy(component => component.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.Version, StringComparer.Ordinal)
            .ToList();

        var timestamp = DateTimeOffset.UtcNow;
        var serialNumber = "urn:uuid:" + Guid.NewGuid();
        var (extension, content) = format == SbomFormat.Json
            ? ("cdx.json", BuildCycloneDxJson(solution.Name, sorted, serialNumber, timestamp))
            : ("cdx.xml", BuildCycloneDxXml(solution.Name, sorted, serialNumber, timestamp));

        var path = Path.Combine(solution.Directory, $"{solution.Name}.{extension}");
        File.WriteAllBytes(path, content);
        return new SbomResult(path, sorted.Count, withoutAssets);
    }

    /// <summary>Reads the NuGet packages from a <c>project.assets.json</c>. False when it is missing or unreadable.</summary>
    private static bool TryReadPackages(string assetsPath, out List<SbomComponent> packages)
    {
        packages = [];
        if (!File.Exists(assetsPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            if (!document.RootElement.TryGetProperty("libraries", out var libraries)
                || libraries.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var library in libraries.EnumerateObject())
            {
                // Keys are "Id/Version"; project references are listed too but are not packages.
                if (!library.Value.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String
                    || type.GetString() != "package")
                {
                    continue;
                }

                var slash = library.Name.IndexOf('/');
                if (slash <= 0 || slash == library.Name.Length - 1)
                {
                    continue;
                }

                var name = library.Name[..slash];
                var version = library.Name[(slash + 1)..];
                var purl = $"pkg:nuget/{Uri.EscapeDataString(name.ToLowerInvariant())}@{Uri.EscapeDataString(version)}";
                packages.Add(new SbomComponent(name, version, purl));
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            packages = [];
            return false;
        }
    }

    private static byte[] BuildCycloneDxJson(
        string solutionName, IReadOnlyList<SbomComponent> components, string serialNumber, DateTimeOffset timestamp)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("bomFormat", "CycloneDX");
            writer.WriteString("specVersion", "1.6");
            writer.WriteString("serialNumber", serialNumber);
            writer.WriteNumber("version", 1);
            writer.WriteStartObject("metadata");
            writer.WriteString("timestamp", timestamp);
            writer.WriteStartObject("component");
            writer.WriteString("type", "application");
            writer.WriteString("name", solutionName);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartArray("components");
            foreach (var component in components)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "library");
                writer.WriteString("bom-ref", component.Purl);
                writer.WriteString("name", component.Name);
                writer.WriteString("version", component.Version);
                writer.WriteString("purl", component.Purl);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static byte[] BuildCycloneDxXml(
        string solutionName, IReadOnlyList<SbomComponent> components, string serialNumber, DateTimeOffset timestamp)
    {
        #pragma warning disable S5332 // The address is fixed by the protocol, the service or the data it points at; it is not a choice the app makes.
        XNamespace ns = "http://cyclonedx.org/schema/bom/1.6";
        #pragma warning restore S5332
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "bom",
                new XAttribute("serialNumber", serialNumber),
                new XAttribute("version", 1),
                new XElement(ns + "metadata",
                    new XElement(ns + "timestamp", timestamp.ToString("O")),
                    new XElement(ns + "component",
                        new XAttribute("type", "application"),
                        new XElement(ns + "name", solutionName))),
                new XElement(ns + "components",
                    components.Select(component => new XElement(ns + "component",
                        new XAttribute("type", "library"),
                        new XAttribute("bom-ref", component.Purl),
                        new XElement(ns + "name", component.Name),
                        new XElement(ns + "version", component.Version),
                        new XElement(ns + "purl", component.Purl))))));

        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

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
            try { process.Kill(entireProcessTree: true); } catch {
                // Deliberately ignored: the failure leaves nothing to undo, so the operation is best effort.
            }
        });

        try
        {
            int exitCode = await exited.Task;

            // The process has exited, but a child it left running (MSBuild keeps a build node alive) can hold the
            // output open. Wait for the last lines a short while, not for the child to finish.
            await Task.WhenAny(Task.Run(() => process.WaitForExit(), cancellationToken), Task.Delay(OutputGracePeriod, cancellationToken));
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
