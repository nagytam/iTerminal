using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Services.Sdk;

/// <summary>
/// Installs .NET SDKs and JDKs into the runtimes folder (<c>dotnet/&lt;version&gt;</c> and <c>jdk/&lt;release&gt;</c>) and
/// lists them. An archive is checked against the digest its publisher lists before it is unpacked, unpacked into a staging
/// folder, and moved into place only when it holds an SDK or a JDK, so a failed install leaves nothing that looks installed.
/// </summary>
public sealed class SdkManager : ISdkManager
{
    /// <summary>The setting that holds the JDK home chosen for Java builds and runs.</summary>
    public const string JavaHomeSetting = "Sdk.JavaHome";

    private readonly HttpClient _httpClient;
    private readonly string _runtimesDirectory;
    private readonly ISettingsService _settings;
    private readonly DotNetSdkFeed _dotNetFeed;
    private readonly JdkFeed _jdkFeed;

    public SdkManager(HttpClient httpClient, string runtimesDirectory, ISettingsService settings)
        : this(httpClient, runtimesDirectory, settings, new DotNetSdkFeed(httpClient), new JdkFeed(httpClient))
    {
    }

    /// <summary>The feeds can be replaced, so a test points them at a fake publisher.</summary>
    internal SdkManager(HttpClient httpClient, string runtimesDirectory, ISettingsService settings, DotNetSdkFeed dotNetFeed, JdkFeed jdkFeed)
    {
        _httpClient = httpClient;
        _runtimesDirectory = runtimesDirectory;
        _settings = settings;
        _dotNetFeed = dotNetFeed;
        _jdkFeed = jdkFeed;
    }

    /// <summary>The folder the .NET SDKs are installed into, one folder per version.</summary>
    public string DotNetRoot => Path.Combine(_runtimesDirectory, "dotnet");

    /// <summary>The folder the JDKs are installed into, one folder per release.</summary>
    public string JdkRoot => Path.Combine(_runtimesDirectory, "jdk");

    /// <inheritdoc />
    public IReadOnlyList<DotnetSdk> InstalledDotNetSdks()
    {
        var sdks = new List<DotnetSdk>();
        foreach (string install in JdkHome.Subfolders(DotNetRoot))
        {
            if (!File.Exists(Path.Combine(install, DotNetHostName))) continue;
            foreach (string sdkFolder in JdkHome.Subfolders(Path.Combine(install, "sdk")))
            {
                sdks.Add(new DotnetSdk(Path.GetFileName(sdkFolder), install));
            }
        }

        return DotnetSdk.Newest(sdks);
    }

    /// <inheritdoc />
    public IReadOnlyList<InstalledJdk> InstalledJdks()
    {
        var jdks = new List<InstalledJdk>();
        foreach (string folder in JdkHome.Subfolders(JdkRoot))
        {
            if (JdkHome.Find(folder) is { } home)
            {
                jdks.Add(new InstalledJdk(Path.GetFileName(folder), folder, home));
            }
        }

        return jdks.OrderByDescending(jdk => JdkHome.FeatureVersion(jdk.Version)).ThenByDescending(jdk => jdk.Version, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public string? ActiveJavaHome
    {
        get
        {
            string? home = _settings.Get(JavaHomeSetting);
            return !string.IsNullOrWhiteSpace(home) && JdkHome.IsJdk(home) ? home : null;
        }
    }

    /// <inheritdoc />
    public void UseJavaHome(string? home)
    {
        _settings.Set(JavaHomeSetting, home ?? string.Empty);
        _settings.Write();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AvailableSdk>> ListAvailableAsync(SdkKind kind, CancellationToken cancellationToken) =>
        kind == SdkKind.DotNet ? ListDotNetAsync(cancellationToken) : ListJdksAsync(cancellationToken);

    private async Task<IReadOnlyList<AvailableSdk>> ListDotNetAsync(CancellationToken cancellationToken) =>
        await _dotNetFeed.ListAsync(DotNetSdkFeed.CurrentRuntimeIdentifier(), OperatingSystem.IsWindows(), cancellationToken);

    private async Task<IReadOnlyList<AvailableSdk>> ListJdksAsync(CancellationToken cancellationToken) =>
        JdkFeed.CurrentArchitecture is { } architecture
            ? await _jdkFeed.ListAsync(JdkFeed.CurrentOs, architecture, cancellationToken)
            : [];

    /// <inheritdoc />
    public async Task<string> InstallAsync(AvailableSdk sdk, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        string parent = sdk.Kind == SdkKind.DotNet ? DotNetRoot : JdkRoot;
        string destination = Path.Combine(parent, SafeName(sdk.Version));
        if (Directory.Exists(destination))
        {
            return destination;
        }

        string archive = Path.Combine(_runtimesDirectory, "downloads", SafeName(sdk.FileName));
        string staging = destination + ".staging";
        try
        {
            await VerifiedDownload.DownloadAsync(_httpClient, sdk.Url, archive, sdk.Size, sdk.Digest, sdk.Algorithm, progress, cancellationToken);
            progress?.Report(string.Format(iConsole.Localization.UiText.Get("SdkManager.Unpacking", "Unpacking {0}…"), sdk.Version));
            DeleteTree(staging);
            ReleaseArchive.Extract(archive, staging);
            if (!IsComplete(sdk, staging))
            {
                throw new InvalidDataException(string.Format(
                    iConsole.Localization.UiText.Get("SdkManager.NotAnInstall", "{0} does not hold a {1} install."), sdk.FileName, sdk.Version));
            }

            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            TryDeleteFile(archive);
            DeleteTree(staging);
        }
    }

    /// <inheritdoc />
    public void Uninstall(string versionFolder)
    {
        string folder = Path.GetFullPath(versionFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetDirectoryName(folder) ?? string.Empty;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool managed = parent.Equals(Path.GetFullPath(DotNetRoot).TrimEnd(Path.DirectorySeparatorChar), comparison)
            || parent.Equals(Path.GetFullPath(JdkRoot).TrimEnd(Path.DirectorySeparatorChar), comparison);
        if (!managed)
        {
            throw new InvalidOperationException("Only an SDK or JDK installed by the SDK manager can be removed.");
        }

        if (!Directory.Exists(folder)) return;
        Directory.Delete(folder, recursive: true);

        // A JDK that was chosen for Java and is now gone goes back to the PATH.
        string? active = _settings.Get(JavaHomeSetting);
        if (!string.IsNullOrEmpty(active) && Path.GetFullPath(active).StartsWith(folder + Path.DirectorySeparatorChar, comparison))
        {
            UseJavaHome(null);
        }
    }

    /// <summary>The install holds what its version says: a .NET host and its SDK folder, or a JDK home with <c>javac</c>.</summary>
    private static bool IsComplete(AvailableSdk sdk, string folder) =>
        sdk.Kind == SdkKind.DotNet
            ? File.Exists(Path.Combine(folder, DotNetHostName)) && Directory.Exists(Path.Combine(folder, "sdk", sdk.Version))
            : JdkHome.Find(folder) is not null;

    private static string DotNetHostName => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    /// <summary>A name that is safe as one folder or file name: anything else becomes an underscore.</summary>
    internal static string SafeName(string name)
    {
        string safe = new(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '+' or '_' ? c : '_').ToArray());
        return safe.Length == 0 || safe.StartsWith('.') ? "_" + safe : safe;
    }

    private static void DeleteTree(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be removed is left for the next install to replace.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The download is removed after the install; one that cannot be removed is left in the downloads folder.
        }
    }
}

/// <summary>Where a JDK's <c>javac</c> is: the folder of an archive, or the <c>Contents/Home</c> folder of a macOS bundle.</summary>
internal static class JdkHome
{
    private static string Javac => OperatingSystem.IsWindows() ? "javac.exe" : "javac";

    /// <summary>The subfolders of a folder, by name; none when it is missing or cannot be read.</summary>
    internal static List<string> Subfolders(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).OrderBy(path => path, StringComparer.Ordinal).ToList() : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }


    /// <summary>The JDK home under <paramref name="folder"/>: the folder itself, one folder below it, or the macOS bundle's home. Null when there is none.</summary>
    public static string? Find(string folder)
    {
        if (IsJdk(folder)) return folder;
        if (IsJdk(Path.Combine(folder, "Contents", "Home"))) return Path.Combine(folder, "Contents", "Home");

        try
        {
            foreach (string sub in Subfolders(folder))
            {
                if (IsJdk(sub)) return sub;
                if (IsJdk(Path.Combine(sub, "Contents", "Home"))) return Path.Combine(sub, "Contents", "Home");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    public static bool IsJdk(string home) => File.Exists(Path.Combine(home, "bin", Javac));

    /// <summary>The feature version a release name carries (<c>jdk-21.0.13.1+2</c> is 21), 0 when it has none.</summary>
    public static int FeatureVersion(string releaseName)
    {
        string digits = new(releaseName.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int version) ? version : 0;
    }
}
