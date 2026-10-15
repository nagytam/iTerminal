using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Git;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A panel on the <c>git://</c> drive. All of the work is <see cref="IGitDriveService"/>'s; this
/// model only names where the panel is in words, since the URL itself carries an escaped path.
/// </summary>
public class ContentModelGitDrive(
    string name,
    ISettingsService? settingsService,
    IGitDriveService gitDriveService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger),
      Services.Shell.IShellLaunchSource
{
    /// <summary>Ctrl+O: a local shell in the repository's working tree — a revision has no folder to stand in.</summary>
    public Services.Shell.ShellLaunch? GetShellLaunch(string? configuredShell)
    {
        GitDriveUrl.Parse(Url, out var repositoryRoot, out _, out _);
        return string.IsNullOrEmpty(repositoryRoot) ? null : Services.Shell.ShellLaunch.Local(repositoryRoot, configuredShell);
    }

    protected override string Scheme => Container.GIT;

    public override ContainerType ContainerType => ContainerType.Git;

    public override IFileSystem FileSystem => gitDriveService;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken) =>
        gitDriveService.ListAsync(url, cancellationToken);

    protected override string DescribeLocation()
    {
        GitDriveUrl.Parse(Url, out var repositoryRoot, out var revision, out var path);
        if (repositoryRoot is null)
        {
            return "git: repositories";
        }

        var repositoryName = Path.GetFileName(repositoryRoot.TrimEnd('\\', '/'));
        if (revision is null)
        {
            return $"git: {repositoryName} — branches, tags, commits";
        }

        return $"git: {repositoryName} @ {ShortRevision(revision)}:/{path}";
    }

    /// <summary>A full commit hash is 40 characters of noise in a title; eight identify it.</summary>
    internal static string ShortRevision(string revision) =>
        revision.Length == 40 && revision.All(Uri.IsHexDigit) ? revision[..8] : revision;
}
