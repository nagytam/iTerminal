namespace iTerminal.DevelopmentStudio.Model.NuGet;

/// <summary>
/// One top-level package reference of one project, as <c>dotnet list package</c> reports it. <see cref="LatestVersion"/> is set
/// only when a newer version exists at the sources; it is null for a package that is up to date.
/// </summary>
/// <param name="ProjectPath">The full path of the project file that references the package.</param>
/// <param name="PackageId">The package id, as the project spells it.</param>
/// <param name="RequestedVersion">The version the project asks for (from the project file, or from the central props).</param>
/// <param name="ResolvedVersion">The version restore picked.</param>
/// <param name="LatestVersion">The newest version at the sources, when that is newer than the resolved one.</param>
public sealed record NuGetPackageUsage(
    string ProjectPath,
    string PackageId,
    string RequestedVersion,
    string ResolvedVersion,
    string? LatestVersion);

/// <summary>
/// What the package listing of a solution or project found. <see cref="Succeeded"/> is false when <c>dotnet list</c> could not
/// report the packages at all (no SDK, a project that does not restore, an old SDK without <c>--format json</c>); the
/// <see cref="Problems"/> then say why. Problems are also set when the packages were listed but something was wrong.
/// </summary>
public sealed record NuGetListing(
    IReadOnlyList<NuGetPackageUsage> Packages,
    IReadOnlyList<string> Problems,
    bool Succeeded);
