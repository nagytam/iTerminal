using iConsole.Data;

namespace iTerminal.Core.Services.Git;

/// <summary>
/// Builds and reads <c>git://&lt;repository&gt;/&lt;revision&gt;/&lt;path&gt;</c> URLs.
///
/// <para>The repository and revision segments are percent-escaped and nothing else is. Both have to
/// be: a repository is a local path (<c>C:\src\app</c> carries a colon and backslashes) and a branch
/// name routinely carries a slash (<c>feature/login</c>), which would otherwise split into two
/// levels — the same reason the music catalog escapes "AC/DC". The tree path is left as it is, so
/// the name a copy takes from <see cref="Url.Name"/> is the file's real name.</para>
/// </summary>
public static class GitDriveUrl
{
    /// <summary>The URL of a repository (no revision), a revision's tree root, or a path inside it.</summary>
    public static Url Build(string repositoryRoot, string? revision = null, string? path = null, bool isFolder = true)
    {
        var url = Container.GIT + Uri.EscapeDataString(repositoryRoot) + "/";
        if (revision is null)
        {
            return new Url(url);
        }

        url += Uri.EscapeDataString(revision) + "/";
        var trimmedPath = (path ?? string.Empty).Trim('/');
        if (trimmedPath.Length > 0)
        {
            url += trimmedPath + (isFolder ? "/" : string.Empty);
        }

        return new Url(url);
    }

    /// <summary>
    /// Splits a git drive URL. <paramref name="repositoryRoot"/> is null at the drive root (the list
    /// of repositories), <paramref name="revision"/> is null at a repository (the list of branches,
    /// tags and commits), and <paramref name="path"/> is the tree path without leading or trailing
    /// slash — empty at a revision's root.
    /// </summary>
    public static void Parse(Url url, out string? repositoryRoot, out string? revision, out string path)
    {
        repositoryRoot = null;
        revision = null;
        path = string.Empty;

        var raw = url.Path;
        var segments = raw.Split('/');
        if (segments.Length == 0 || segments[0].Length == 0)
        {
            return;
        }

        repositoryRoot = Uri.UnescapeDataString(segments[0]);
        if (segments.Length < 2 || segments[1].Length == 0)
        {
            return;
        }

        revision = Uri.UnescapeDataString(segments[1]);
        path = string.Join('/', segments.Skip(2)).Trim('/');
    }

    /// <summary>The <c>&lt;revision&gt;:&lt;path&gt;</c> object name git resolves a tree or blob by.</summary>
    public static string ObjectName(string revision, string path) => $"{revision}:{path}";
}
