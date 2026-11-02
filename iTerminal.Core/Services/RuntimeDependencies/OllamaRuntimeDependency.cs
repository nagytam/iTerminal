using System.Formats.Tar;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
    private readonly IReadOnlyList<string> _searchDirectories;
    private readonly object _existingGate = new();
    private ExistingInstall? _existing;
    private bool _existingResolved;

    public OllamaRuntimeDependency(string rootDirectory, string model, OllamaServer server)
        : this(rootDirectory, model, server, new GitHubReleaseFeed(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }), Built, DefaultSearchDirectories())
    {
    }

    /// <param name="existingSearchDirectories">Where an Ollama installed outside iTerminal is looked for; null looks nowhere, which is what a test wants.</param>
    internal OllamaRuntimeDependency(
        string rootDirectory,
        string model,
        OllamaServer server,
        GitHubReleaseFeed feed,
        Version expected,
        IEnumerable<string>? existingSearchDirectories = null)
    {
        _root = rootDirectory;
        _model = model;
        _server = server;
        _feed = feed;
        _expected = expected;
        _searchDirectories = existingSearchDirectories?.ToList() ?? [];
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
        if (OwnCompleteVersion() is { } own)
        {
            return own;
        }

        // An Ollama that was there already counts too, once the configured model is in it.
        ExistingInstall? existing = Existing();
        return existing is not null && (ModelDownloaded() || existing.HasModel) ? existing.Version : null;
    }

    /// <summary>True when the version reported is an install outside the data folder, which is never updated from here.</summary>
    public bool IsExistingInstall => OwnCompleteVersion() is null && InstalledVersion() is not null;

    public string? ExistingInstallNote
    {
        get
        {
            if (OwnCompleteVersion() is not null || Existing() is not { } existing)
            {
                return null;
            }

            string where = existing.BinaryPath ?? _server.Address;
            return InstalledVersion() is null
                ? $"Ollama is already installed ({where}); only the {_model} model is downloaded."
                : $"Using the Ollama that is already installed ({where}).";
        }
    }

    /// <summary>The newest complete version in the data folder with the model downloaded, or null.</summary>
    private string? OwnCompleteVersion() =>
        ModelDownloaded() ? CompleteVersions().OrderDescending().Select(v => v.ToString(3)).FirstOrDefault() : null;

    /// <summary>
    /// The program to start the server from: the newest one downloaded by iTerminal, otherwise an Ollama that
    /// was installed before. Null when there is none.
    /// </summary>
    internal string? BinaryPath()
    {
        string? installed = CompleteVersions().OrderDescending().Select(v => v.ToString(3)).FirstOrDefault();
        if (installed is not null)
        {
            return FindBinary(VersionDirectory(installed));
        }

        return Existing()?.BinaryPath;
    }

    /// <summary>
    /// The models folder to start a given program with: the shared one for a program iTerminal downloaded, and
    /// null for an existing install, which keeps its models where it always did.
    /// </summary>
    internal string? ModelsDirectoryFor(string binaryPath) =>
        Path.GetFullPath(binaryPath).StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? ModelsDirectory
            : null;

    public async Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        // The check is also where a running server is looked for: asking it is a network call, which
        // InstalledVersion (read from the render thread) must never make.
        await ProbeExistingAsync(cancellationToken);
        return await _feed.ListVersionsAsync(Repository, cancellationToken);
    }

    private async Task ProbeExistingAsync(CancellationToken cancellationToken)
    {
        OllamaServerInfo? info = await _server.GetInfoAsync(cancellationToken);
        ExistingInstall? existing = BuildExisting(info);
        lock (_existingGate)
        {
            _existing = existing;
            _existingResolved = true;
        }
    }

    /// <summary>What is known of an Ollama outside the data folder: a running server's answer, and/or a program on disk.</summary>
    private ExistingInstall? Existing()
    {
        lock (_existingGate)
        {
            if (_existingResolved)
            {
                return _existing;
            }
        }

        // Nothing has asked a server yet: what is on disk is what is known.
        ExistingInstall? onDisk = BuildExisting(null);
        lock (_existingGate)
        {
            if (!_existingResolved)
            {
                _existing = onDisk;
                _existingResolved = true;
            }

            return _existing;
        }
    }

    private ExistingInstall? BuildExisting(OllamaServerInfo? info)
    {
        string? binary = FindExistingBinary(_searchDirectories, _root);
        if (binary is null && info is null)
        {
            return null;
        }

        string? version = info?.Version;
        if (!Version.TryParse(version, out _) && binary is not null)
        {
            version = ReadProgramVersion(binary);
        }

        // A version that cannot be read still means "installed": 0.0.0 is only the placeholder shown for it.
        version = Version.TryParse(version, out Version? parsed) ? parsed.ToString(3) : "0.0.0";
        return new ExistingInstall(binary, version, info?.HasModel(_model) ?? false);
    }

    /// <summary>The places an Ollama installed outside iTerminal is looked for: the PATH, then each system's usual folders.</summary>
    internal static IEnumerable<string> DefaultSearchDirectories()
    {
        List<string> directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            directories.Add(Path.Combine(local, "Programs", "Ollama"));
            directories.Add(Path.Combine(programFiles, "Ollama"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            directories.AddRange(["/Applications/Ollama.app/Contents/Resources", "/usr/local/bin", "/opt/homebrew/bin"]);
        }
        else
        {
            directories.AddRange(["/usr/local/bin", "/usr/bin", "/opt/ollama/bin", "/snap/bin"]);
        }

        return directories.Where(directory => directory.Length > 0);
    }

    /// <summary>The first <c>ollama</c> program in <paramref name="directories"/> that is not inside <paramref name="ownRoot"/>.</summary>
    internal static string? FindExistingBinary(IEnumerable<string> directories, string ownRoot)
    {
        string own = Path.GetFullPath(ownRoot) + Path.DirectorySeparatorChar;
        string name = OperatingSystem.IsWindows() ? "ollama.exe" : "ollama";
        foreach (string directory in directories)
        {
            try
            {
                string candidate = Path.GetFullPath(Path.Combine(directory, name));
                if (!candidate.StartsWith(own, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A malformed PATH entry.
            }
        }

        return null;
    }

    /// <summary>The version an Ollama program reports: from its file when that carries one, otherwise by asking it.</summary>
    private static string? ReadProgramVersion(string binary)
    {
        try
        {
            string? fromFile = FileVersionInfo.GetVersionInfo(binary).ProductVersion;
            if (Version.TryParse(fromFile, out Version? fileVersion) && fileVersion > new Version(0, 0, 0))
            {
                return fileVersion.ToString(3);
            }

            using Process? process = Process.Start(new ProcessStartInfo(binary, "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
            {
                return null;
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            Match match = Regex.Match(output.GetAwaiter().GetResult() + " " + error.GetAwaiter().GetResult(), @"\d+\.\d+\.\d+");
            return match.Success ? match.Value : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    private sealed record ExistingInstall(string? BinaryPath, string Version, bool HasModel);

    public async Task InstallAsync(string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Version.Parse(version); // A malformed version is refused before anything is written.
        string target = VersionDirectory(version);

        // Ollama that is already on this computer (a program, or a server that answers) is used instead of a second copy,
        // unless iTerminal has a copy of its own: that one is being updated, so the new version is downloaded.
        bool useExisting = false;
        string? existingBinary = null;
        if (FindBinary(target) is null && !CompleteVersions().Any())
        {
            existingBinary = FindExistingBinary(_searchDirectories, _root);
            useExisting = existingBinary is not null || await _server.IsReachableAsync(cancellationToken);
        }

        if (useExisting)
        {
            progress.Report($"Ollama is already installed ({existingBinary ?? _server.Address}); it is used as it is");
        }
        else if (FindBinary(target) is null)
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
            bool started = false;
            try
            {
                if (useExisting)
                {
                    // A server that answers is used as it is; otherwise the program found is started with its own models folder.
                    if (existingBinary is not null)
                    {
                        progress.Report($"Starting {existingBinary} to download {_model}");
                        started = await _server.EnsureRunningAsync(existingBinary, null, cancellationToken);
                    }

                    OllamaServerInfo? info = await _server.GetInfoAsync(cancellationToken);
                    if (info is not null && info.HasModel(_model))
                    {
                        progress.Report($"{_model} is already downloaded");
                    }
                    else
                    {
                        progress.Report($"Downloading {_model}");
                        await _server.PullAsync(_model, progress, cancellationToken);
                    }

                    // Looked at while the server is up, so the install is reported with its version and model afterwards.
                    await ProbeExistingAsync(cancellationToken);
                }
                else
                {
                    string binary = FindBinary(target)!;
                    progress.Report($"Starting Ollama {version} to download {_model}");
                    started = await _server.EnsureRunningAsync(binary, ModelsDirectory, cancellationToken);
                    progress.Report($"Downloading {_model}");
                    await _server.PullAsync(_model, progress, cancellationToken);
                }
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

        if (useExisting)
        {
            progress.Report($"Ollama is ready with {_model}");
            return;
        }

        PruneOlderVersions(version);
        progress.Report($"Ollama {version} is ready with {_model}");
    }

    /// <summary>Nothing is process-wide: the server is started with its own environment when it is needed.</summary>
    public void Activate()
    {
    }

    /// <summary>Forgets the model download, so the dependency is reported missing and downloaded again.</summary>
    public void Invalidate()
    {
        TryDeleteFile(Path.Combine(_root, ModelMarker));
        lock (_existingGate)
        {
            // The next check looks again: the model a server held may be gone as well.
            _existing = null;
            _existingResolved = false;
        }
    }

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
