using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using iTerminal.Core.AI;
using ZstdSharp;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// Ollama, the local model server the AI prompt bar talks to. The server program comes from the GitHub release
/// of the version this build was tested against (its line is updated the way Playwright's is), and the model the
/// AI bar is configured for is downloaded into that server. Nothing is installed system-wide.
/// </summary>
/// <remarks>
/// Layout under <c>&lt;root&gt;</c>:
/// <list type="bullet">
/// <item><c>&lt;version&gt;/</c>: the release as unpacked; the program is <c>bin/ollama</c> (Linux), <c>ollama</c> (macOS) or <c>ollama.exe</c> (Windows).</item>
/// <item><c>models</c>: the models, shared by every version (<c>OLLAMA_MODELS</c>).</item>
/// <item><c>model.installed</c>: the name of the model that was downloaded last; written once that download finished.</item>
/// </list>
/// </remarks>
public sealed class OllamaRuntimeDependency : IRuntimeDependency
{
    public const string DependencyId = "ollama";

    /// <summary>The Ollama version this build is tested against; its line is the one an update stays on.</summary>
    internal static readonly Version Built = new(0, 40, 4);

    internal const string Repository = "ollama/ollama";
    internal const string ModelsFolder = "models";
    internal const string ModelMarker = "model.installed";

    private const string DownloadsFolder = "downloads";

    private readonly string _root;
    private readonly string _model;
    private readonly OllamaServer _server;
    private readonly GitHubReleaseFeed _feed;
    private readonly Version _expected;

    public OllamaRuntimeDependency(string rootDirectory, string model, OllamaServer server)
        : this(rootDirectory, model, server, new GitHubReleaseFeed(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }), Built)
    {
    }

    internal OllamaRuntimeDependency(string rootDirectory, string model, OllamaServer server, GitHubReleaseFeed feed, Version expected)
    {
        _root = rootDirectory;
        _model = model;
        _server = server;
        _feed = feed;
        _expected = expected;
    }

    public string Id => DependencyId;

    public string Name => "Ollama";

    public string Purpose => $"Local AI server and the {_model} model for the AI prompt bar";

    public string ExpectedVersion => _expected.ToString(3);

    public bool IsCompatible(Version version) => version.Major == _expected.Major && version.Minor == _expected.Minor;

    /// <summary>The folder the models are kept in, shared by every version.</summary>
    internal string ModelsDirectory => Path.Combine(_root, ModelsFolder);

    /// <summary>
    /// The newest complete version, once the configured model is downloaded as well. Without the model the
    /// install is incomplete, so the dependency is reported missing and the download is offered again.
    /// </summary>
    public string? InstalledVersion()
    {
        if (!ModelDownloaded())
        {
            return null;
        }

        return CompleteVersions().OrderDescending().Select(v => v.ToString(3)).FirstOrDefault();
    }

    /// <summary>The program of the newest complete version, or null when none is installed.</summary>
    internal string? BinaryPath()
    {
        string? installed = CompleteVersions().OrderDescending().Select(v => v.ToString(3)).FirstOrDefault();
        return installed is null ? null : FindBinary(VersionDirectory(installed));
    }

    public async Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken) =>
        await _feed.ListVersionsAsync(Repository, cancellationToken);

    public async Task InstallAsync(string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Version.Parse(version); // A malformed version is refused before anything is written.
        string target = VersionDirectory(version);

        if (FindBinary(target) is null)
        {
            GitHubRelease release = await _feed.GetReleaseAsync(Repository, version, cancellationToken);
            GitHubAsset asset = AssetFor(release, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture)
                ?? throw new InvalidOperationException($"Ollama {version} has no download for {PlatformName()}.");

            string downloads = Path.Combine(_root, DownloadsFolder);
            Directory.CreateDirectory(downloads);
            string package = Path.Combine(downloads, asset.Name);
            try
            {
                await _feed.DownloadAsync(asset, package, progress, cancellationToken);
                progress.Report($"Unpacking Ollama {version}");
                string staging = target + ".staging";
                DeleteDirectory(staging);
                Extract(package, staging);
                if (FindBinary(staging) is null)
                {
                    throw new InvalidDataException($"The Ollama {version} download has no program in it.");
                }

                DeleteDirectory(target);
                Directory.Move(staging, target);
            }
            finally
            {
                TryDeleteFile(package);
                TryDeleteEmptyDirectory(downloads);
            }
        }

        if (!ModelDownloaded())
        {
            string binary = FindBinary(target)!;
            progress.Report($"Starting Ollama {version} to download {_model}");
            bool started = await _server.EnsureRunningAsync(binary, ModelsDirectory, cancellationToken);
            try
            {
                progress.Report($"Downloading {_model}");
                await _server.PullAsync(_model, progress, cancellationToken);
            }
            finally
            {
                if (started)
                {
                    _server.Stop();
                }
            }

            await File.WriteAllTextAsync(Path.Combine(_root, ModelMarker), _model, cancellationToken);
        }

        PruneOlderVersions(version);
        progress.Report($"Ollama {version} is ready with {_model}");
    }

    /// <summary>Nothing is process-wide: the server is started with its own environment when it is needed.</summary>
    public void Activate()
    {
    }

    /// <summary>Forgets the model download, so the dependency is reported missing and downloaded again.</summary>
    public void Invalidate() => TryDeleteFile(Path.Combine(_root, ModelMarker));

    private bool ModelDownloaded()
    {
        string marker = Path.Combine(_root, ModelMarker);
        try
        {
            return File.Exists(marker) && File.ReadAllText(marker).Trim() == _model;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private IEnumerable<Version> CompleteVersions()
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        foreach (string directory in Directory.EnumerateDirectories(_root))
        {
            if (Version.TryParse(Path.GetFileName(directory), out Version? version)
                && IsCompatible(version)
                && FindBinary(directory) is not null)
            {
                yield return version;
            }
        }
    }

    private string VersionDirectory(string version) => Path.Combine(_root, version);

    /// <summary>The program in an unpacked release, wherever this platform's archive puts it; null when there is none.</summary>
    internal static string? FindBinary(string directory)
    {
        string[] candidates = [Path.Combine(directory, "bin", "ollama"), Path.Combine(directory, "ollama"), Path.Combine(directory, "ollama.exe")];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// The release asset for a platform. Linux ships <c>.tar.zst</c>, Windows a <c>.zip</c>, macOS a universal
    /// <c>.tgz</c>; the names are GitHub's, and an asset missing from the release is reported rather than guessed.
    /// </summary>
    internal static GitHubAsset? AssetFor(GitHubRelease release, bool windows, bool macOS, Architecture architecture)
    {
        string? name = AssetName(windows, macOS, architecture);
        return name is null ? null : release.Assets.FirstOrDefault(asset => asset.Name == name);
    }

    internal static string? AssetName(bool windows, bool macOS, Architecture architecture)
    {
        string? arch = architecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };

        if (arch is null)
        {
            return null;
        }

        if (windows)
        {
            return $"ollama-windows-{arch}.zip";
        }

        if (macOS)
        {
            return "ollama-darwin.tgz";
        }

        return $"ollama-linux-{arch}.tar.zst";
    }

    private static string PlatformName() =>
        $"{(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux")} {RuntimeInformation.ProcessArchitecture}";

    /// <summary>
    /// Unpacks a release into <paramref name="destination"/>. Every entry must land inside it (the "zip slip");
    /// one that would not is skipped. Symbolic links are kept when they stay inside as well, because the
    /// program's GPU libraries are linked that way.
    /// </summary>
    internal static void Extract(string package, string destination)
    {
        Directory.CreateDirectory(destination);
        string destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        if (package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using ZipArchive archive = ZipFile.OpenRead(package);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                string? target = Inside(destination, destinationRoot, entry.FullName);
                if (target is null)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            return;
        }

        using Stream file = File.OpenRead(package);
        using Stream archiveStream = package.EndsWith(".tar.zst", StringComparison.OrdinalIgnoreCase)
            ? new DecompressionStream(file)
            : new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(archiveStream);
        while (tar.GetNextEntry() is { } tarEntry)
        {
            string? target = Inside(destination, destinationRoot, tarEntry.Name);
            if (target is null)
            {
                continue;
            }

            switch (tarEntry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;

                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    tarEntry.ExtractToFile(target, overwrite: true);
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(target, tarEntry.Mode);
                    }

                    break;

                case TarEntryType.SymbolicLink when !OperatingSystem.IsWindows():
                    // Only a link whose target also stays inside the destination.
                    string linkTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, tarEntry.LinkName));
                    if (!linkTarget.StartsWith(destinationRoot, StringComparison.Ordinal))
                    {
                        break;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    TryDeleteFile(target);
                    File.CreateSymbolicLink(target, tarEntry.LinkName);
                    break;

                default:
                    // Hard links and devices have no place in a release of this program.
                    break;
            }
        }
    }

    /// <summary>The full path of an archive entry under <paramref name="destination"/>, or null when it would land outside it.</summary>
    private static string? Inside(string destination, string destinationRoot, string entryName)
    {
        string target = Path.GetFullPath(Path.Combine(destination, entryName.Replace('/', Path.DirectorySeparatorChar)));
        return target.StartsWith(destinationRoot, StringComparison.Ordinal) ? target : null;
    }

    private bool IsVersionDirectory(string directory) =>
        Version.TryParse(Path.GetFileName(directory), out Version? version) && IsCompatible(version);

    /// <summary>Removes the versions on this line that are older than the one just installed.</summary>
    private void PruneOlderVersions(string installed)
    {
        Version current = Version.Parse(installed);
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(_root).Where(IsVersionDirectory))
        {
            if (Version.Parse(Path.GetFileName(directory)) < current)
            {
                DeleteDirectory(directory);
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file in it is in use (on Windows, a server still running from it). The next install tries again.
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            Directory.Delete(path);
        }
        catch (IOException)
        {
            // Not empty, or in use: the next install reuses it.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
