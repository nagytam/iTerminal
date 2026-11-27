using iTerminal.DevelopmentStudio.Model.NuGet;

namespace iTerminal.DevelopmentStudio.Services.NuGet;

/// <summary>
/// The NuGet packages of a solution or a project, read and changed through the <c>dotnet</c> CLI of the SDK the studio uses.
/// Only top-level package references are listed; transitive packages are not managed here.
/// </summary>
public interface INuGetPackageService
{
    /// <summary>
    /// The project files a target stands for: the projects of a <c>.sln</c> or <c>.slnx</c>, or the project itself. Reads the
    /// solution file only.
    /// </summary>
    IReadOnlyList<string> ProjectsOf(string targetPath);

    /// <summary>
    /// The top-level packages of <paramref name="targetPath"/> (a solution or a project), with the newest version of each one
    /// that is outdated. Runs <c>dotnet list package</c> twice; it may reach the package sources. Never throws for a failed command:
    /// the failure is in the listing.
    /// </summary>
    Task<NuGetListing> ListAsync(string targetPath, string dotnetHost, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>dotnet add &lt;project&gt; package &lt;id&gt;</c>, with <c>--version</c> when <paramref name="version"/> is set, otherwise the
    /// newest version. Every line the command prints goes to <paramref name="onOutputLine"/>. Returns the exit code.
    /// </summary>
    Task<int> AddAsync(string projectPath, string packageId, string? version, string dotnetHost, Action<string> onOutputLine, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>dotnet remove &lt;project&gt; package &lt;id&gt;</c>. Every line the command prints goes to <paramref name="onOutputLine"/>.
    /// Returns the exit code.
    /// </summary>
    Task<int> RemoveAsync(string projectPath, string packageId, string dotnetHost, Action<string> onOutputLine, CancellationToken cancellationToken = default);
}
