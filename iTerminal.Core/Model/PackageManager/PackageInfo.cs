namespace iTerminal.Core.Model.PackageManager;

/// <summary>
/// One row from a package manager's list/search output (e.g. <c>winget list</c>/<c>search</c>,
/// <c>dpkg-query</c>/<c>apt-cache search</c>). <see cref="AvailableVersion"/> is set only for an
/// installed package that has an update available.
/// </summary>
public sealed record PackageInfo(string Name, string Id, string Version, string? AvailableVersion, string Source);
