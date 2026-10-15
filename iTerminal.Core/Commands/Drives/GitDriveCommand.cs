using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services.Git;

namespace iTerminal.Core.Commands.Drives;

/// <summary>
/// Ctrl+F6: the history of the repository the focused panel is in, as a drive — its branches, tags
/// and recent commits, each a folder holding the tree as it was. Outside a repository it opens the
/// drive's root, the repositories browsed before. See <see cref="OpenDriveCommand"/>.
///
/// <para>The repository is found by walking up for a <c>.git</c> entry rather than by asking
/// <c>git rev-parse</c>: this runs on the key handler, and a few existence checks fit its budget
/// where a process spawn may not.</para>
/// </summary>
public class GitDriveCommand(IContentModelService contentModelService) : OpenDriveCommand(contentModelService)
{
    public override string Title => "Git history";
    public override string Description => "Browse this repository's branches, tags and commits as folders; press again to go back.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.F6, false, false, true);

    protected override ContainerType DriveType => ContainerType.Git;

    protected override Url Destination(IContentModel panel)
    {
        var root = panel.Url.ContainerType == ContainerType.FileSystem
            ? FindRepositoryRoot(panel.Url.FullName)
            : null;
        return root is null ? new Url(Container.GIT) : GitDriveUrl.Build(root);
    }

    /// <summary>The nearest folder at or above <paramref name="folder"/> holding a <c>.git</c> folder or file (a worktree).</summary>
    internal static string? FindRepositoryRoot(string folder)
    {
        try
        {
            for (var directory = new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
            {
                var dotGit = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(dotGit) || File.Exists(dotGit))
                {
                    return directory.FullName.TrimEnd(Path.DirectorySeparatorChar);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return null;
    }
}
