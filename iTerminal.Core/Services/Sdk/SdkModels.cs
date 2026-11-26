using System.Security.Cryptography;
using iTerminal.Core.Model.Dotnet;

namespace iTerminal.Core.Services.Sdk;

/// <summary>What the SDK manager installs: a .NET SDK, or a Java development kit (JDK).</summary>
public enum SdkKind
{
    DotNet,
    Jdk,
}

/// <summary>
/// One SDK or JDK the manager can download for this computer: where its archive comes from, what the archive must hash
/// to, and whether it is the newest of its channel.
/// </summary>
/// <param name="Kind">.NET SDK or JDK.</param>
/// <param name="Version">The version: <c>10.0.404</c> for a .NET SDK, the release name <c>jdk-21.0.13.1+2</c> for a JDK.</param>
/// <param name="Channel">The feature line: the .NET channel (<c>10.0</c>) or the JDK feature version (<c>21</c>).</param>
/// <param name="Url">The archive's address.</param>
/// <param name="FileName">The archive's file name.</param>
/// <param name="Digest">The lower-case hexadecimal digest the publisher lists for the archive.</param>
/// <param name="Algorithm">SHA-512 for the .NET release feed, SHA-256 for Adoptium.</param>
/// <param name="Size">The archive's size in bytes when the publisher lists it.</param>
/// <param name="IsLatest">True for the newest release of its channel.</param>
public sealed record AvailableSdk(
    SdkKind Kind,
    string Version,
    string Channel,
    string Url,
    string FileName,
    string Digest,
    HashAlgorithmName Algorithm,
    long? Size,
    bool IsLatest);

/// <summary>A JDK installed in the runtimes folder.</summary>
/// <param name="Version">The release name the folder is named after.</param>
/// <param name="Folder">The version folder (what uninstalling removes).</param>
/// <param name="Home">The JDK home: the folder whose <c>bin</c> holds <c>javac</c> and <c>java</c>.</param>
public sealed record InstalledJdk(string Version, string Folder, string Home);

/// <summary>
/// The SDKs and JDKs the Development studio uses: the ones installed here (downloaded into the runtimes folder), the
/// Java chosen for builds and runs, and the downloads that install more.
/// </summary>
public interface ISdkManager
{
    /// <summary>The .NET SDKs downloaded into the runtimes folder, newest first. Their <c>dotnet</c> hosts run builds with them.</summary>
    IReadOnlyList<DotnetSdk> InstalledDotNetSdks();

    /// <summary>The JDKs downloaded into the runtimes folder.</summary>
    IReadOnlyList<InstalledJdk> InstalledJdks();

    /// <summary>The JDK home the Development studio builds and runs Java with, or null for <c>javac</c> and <c>java</c> on the PATH.</summary>
    string? ActiveJavaHome { get; }

    /// <summary>Chooses the JDK home for Java builds and runs; null goes back to the PATH.</summary>
    void UseJavaHome(string? home);

    /// <summary>The SDKs or JDKs that can be downloaded for this computer, newest first. Throws when the publisher cannot be reached.</summary>
    Task<IReadOnlyList<AvailableSdk>> ListAvailableAsync(SdkKind kind, CancellationToken cancellationToken);

    /// <summary>Downloads, checks and unpacks one SDK or JDK. An install that is already there is returned as it is.</summary>
    Task<string> InstallAsync(AvailableSdk sdk, IProgress<string>? progress, CancellationToken cancellationToken);

    /// <summary>Removes an install from the runtimes folder. Only a folder directly under the .NET or JDK folder is removed.</summary>
    void Uninstall(string versionFolder);
}
