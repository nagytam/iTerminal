using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Processes;
using System.Globalization;

namespace iTerminal.Core.Services.Git;

/// <summary>
/// The <c>git://</c> drive: a repository's history browsed as folders. Three levels —
/// the repositories that have been opened, then a repository's branches, tags and recent commits,
/// then any of those revisions' trees exactly as they were committed.
///
/// <para>Read-only by nature: a commit cannot be written to. What the drive is for is reading an
/// old version (F3), copying it out (F5 to a disk panel), or previewing it next to the current
/// file — all of which go through <see cref="StreamToAsync"/>, which pipes <c>git cat-file</c>
/// straight into the destination without a temporary checkout.</para>
///
/// <para>Every listing is one or two process runs; none is made from a key handler — the model
/// lists on a background task.</para>
/// </summary>
public sealed class GitDriveService(IExternalProcessRunner processRunner, ISettingsService? settingsService = null) : IGitDriveService
{
    /// <summary>Setting holding the repositories listed at the drive root, newest first, one per line.</summary>
    public const string RepositoriesSettingKey = "GitDriveRepositories";

    /// <summary>How many commits a repository's listing goes back. Older ones are reachable through a branch.</summary>
    public const int RecentCommitCount = 100;

    private const int MaxRememberedRepositories = 20;

    private readonly ISettingsService _settingsService = settingsService ?? NullSettingsService.Instance;

    public bool IsReadOnly => true;

    #region Repositories

    public IReadOnlyList<string> Repositories =>
        (_settingsService.Get(RepositoriesSettingKey) ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void RememberRepository(string repositoryRoot)
    {
        var list = Repositories
            .Where(r => !string.Equals(r, repositoryRoot, StringComparison.OrdinalIgnoreCase))
            .Prepend(repositoryRoot)
            .Take(MaxRememberedRepositories);
        _settingsService.Set(RepositoriesSettingKey, string.Join('\n', list));
    }

    #endregion

    #region Listing

    public async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        GitDriveUrl.Parse(url, out var repositoryRoot, out var revision, out var path);
        if (repositoryRoot is null)
        {
            return ListRepositories();
        }

        if (revision is null)
        {
            RememberRepository(repositoryRoot);
            return await ListRevisionsAsync(repositoryRoot, cancellationToken);
        }

        return await ListTreeAsync(repositoryRoot, revision, path, cancellationToken);
    }

    private List<IEntry> ListRepositories()
    {
        List<IEntry> entries = [];
        foreach (var repository in Repositories)
        {
            var name = Path.GetFileName(repository.TrimEnd('\\', '/'));
            entries.Add(new VirtualDriveEntry(GitDriveUrl.Build(repository), string.IsNullOrEmpty(name) ? repository : name, "Repo")
            {
                Detail = repository,
            });
        }

        return entries;
    }

    private async Task<IReadOnlyList<IEntry>> ListRevisionsAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        List<IEntry> entries = [];

        // HEAD first: "what is checked out" is the revision the user is most likely to want.
        var head = await RunGitAsync(repositoryRoot, cancellationToken, "log", "-1", "--format=%H%x09%ct%x09%s");
        if (head.Success && TrySplit(head.StandardOutput.Trim(), 3, out var headFields))
        {
            var branch = await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD");
            var branchName = branch.Success ? branch.StandardOutput.Trim() : string.Empty;
            entries.Add(new VirtualDriveEntry(GitDriveUrl.Build(repositoryRoot, "HEAD"), "HEAD", "Head", FromUnix(headFields[1]))
            {
                Detail = branchName.Length > 0 && branchName != "HEAD" ? $"HEAD → {branchName}" : headFields[2],
            });
        }
        else if (!head.Success)
        {
            throw new InvalidOperationException(head.FirstErrorLine);
        }

        var refs = await RunGitAsync(
            repositoryRoot,
            cancellationToken,
            "for-each-ref",
            "--sort=-committerdate",
            "--format=%(refname)%09%(refname:short)%09%(committerdate:unix)%09%(subject)",
            "refs/heads",
            "refs/tags",
            "refs/remotes");
        if (refs.Success)
        {
            foreach (var line in Lines(refs.StandardOutput))
            {
                if (!TrySplit(line, 4, out var fields) || fields[0].EndsWith("/HEAD", StringComparison.Ordinal))
                {
                    continue;
                }

                var kind = fields[0].StartsWith("refs/tags/", StringComparison.Ordinal) ? "Tag"
                    : fields[0].StartsWith("refs/remotes/", StringComparison.Ordinal) ? "Remote"
                    : "Branch";
                entries.Add(new VirtualDriveEntry(GitDriveUrl.Build(repositoryRoot, fields[1]), fields[1], kind, FromUnix(fields[2]))
                {
                    Detail = $"{kind}: {fields[3]}",
                });
            }
        }

        var log = await RunGitAsync(
            repositoryRoot,
            cancellationToken,
            "log",
            $"-{RecentCommitCount}",
            "--format=%H%x09%h%x09%ct%x09%s");
        if (log.Success)
        {
            foreach (var line in Lines(log.StandardOutput))
            {
                if (!TrySplit(line, 4, out var fields))
                {
                    continue;
                }

                entries.Add(new VirtualDriveEntry(GitDriveUrl.Build(repositoryRoot, fields[0]), $"{fields[1]} {fields[3]}", "Commit", FromUnix(fields[2]))
                {
                    Detail = $"Commit {fields[1]}: {fields[3]}",
                });
            }
        }

        return entries;
    }

    private async Task<IReadOnlyList<IEntry>> ListTreeAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken)
    {
        var tree = await RunGitAsync(repositoryRoot, cancellationToken, "ls-tree", "-z", "-l", GitDriveUrl.ObjectName(revision, path));
        if (!tree.Success)
        {
            throw new InvalidOperationException(tree.FirstErrorLine);
        }

        // One date for the whole listing — the revision's own. A per-file "last changed" date is a
        // log walk per row, which is exactly the work this drive must not do on every navigation.
        var date = await RunGitAsync(repositoryRoot, cancellationToken, "log", "-1", "--format=%ct", revision);
        DateTime? modified = date.Success ? FromUnix(date.StandardOutput.Trim()) : null;

        List<IEntry> folders = [];
        List<IEntry> files = [];
        foreach (var item in ParseTree(tree.StandardOutput))
        {
            var childPath = path.Length > 0 ? $"{path}/{item.Name}" : item.Name;
            switch (item.Type)
            {
                case "tree":
                    folders.Add(new VirtualDriveEntry(GitDriveUrl.Build(repositoryRoot, revision, childPath, isFolder: true), item.Name, Url.Folder, modified));
                    break;
                case "blob":
                    files.Add(new VirtualDriveEntry(GitDriveUrl.Build(repositoryRoot, revision, childPath, isFolder: false), item.Name, item.Size.ToFileSize(), modified)
                    {
                        Size = item.Size,
                    });
                    break;
                case "commit":
                    // A submodule: its tree lives in another repository, so there is nothing to open.
                    files.Add(new VirtualDriveEntry(GitDriveUrl.Build(repositoryRoot, revision, childPath, isFolder: false), item.Name, "Submod", modified)
                    {
                        Detail = $"Submodule at {item.ObjectId[..Math.Min(8, item.ObjectId.Length)]}",
                    });
                    break;
            }
        }

        folders.Sort(CompareByName);
        files.Sort(CompareByName);
        return [.. folders, .. files];
    }

    private static int CompareByName(IEntry a, IEntry b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName);

    internal readonly record struct TreeItem(string Mode, string Type, string ObjectId, long Size, string Name);

    /// <summary>
    /// Reads <c>git ls-tree -z -l</c>: NUL-terminated records of
    /// <c>&lt;mode&gt; SP &lt;type&gt; SP &lt;object&gt; SP+ &lt;size&gt; TAB &lt;name&gt;</c>.
    /// The size is right-aligned with spaces and is <c>-</c> for a tree. With <c>-z</c> names come
    /// verbatim — no quoting of spaces or non-ASCII to undo.
    /// </summary>
    internal static IEnumerable<TreeItem> ParseTree(string output)
    {
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            var meta = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (meta.Length < 4)
            {
                continue;
            }

            long.TryParse(meta[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
            yield return new TreeItem(meta[0], meta[1], meta[2], size, record[(tab + 1)..]);
        }
    }

    public IEnumerable<Url> ListChildren(Url folderUrl)
    {
        foreach (var entry in ListAsync(folderUrl, CancellationToken.None).GetAwaiter().GetResult())
        {
            yield return entry.Url;
        }
    }

    #endregion

    #region Reading

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (repositoryRoot, revision, path) = RequireBlob(url);
        var counting = new ProgressStream(destination, progress);
        var result = await processRunner.RunToStreamAsync(
            "git",
            ["-C", repositoryRoot, "cat-file", "blob", GitDriveUrl.ObjectName(revision, path)],
            counting,
            cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new IOException(result.FirstErrorLine);
        }
    }

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var buffer = new MemoryStream();
        await StreamToAsync(url, buffer, progress: null, CancellationToken.None);
        return new MemoryFile(url.Name, buffer.ToArray());
    }

    public bool ExistsFile(Url url) => ObjectType(url) == "blob";

    public bool ExistsFolder(Url url)
    {
        GitDriveUrl.Parse(url, out var repositoryRoot, out var revision, out _);
        return repositoryRoot is null || revision is null || ObjectType(url) == "tree";
    }

    private string? ObjectType(Url url)
    {
        GitDriveUrl.Parse(url, out var repositoryRoot, out var revision, out var path);
        if (repositoryRoot is null || revision is null)
        {
            return null;
        }

        var result = RunGitAsync(repositoryRoot, CancellationToken.None, "cat-file", "-t", GitDriveUrl.ObjectName(revision, path))
            .GetAwaiter().GetResult();
        return result.Success ? result.StandardOutput.Trim() : null;
    }

    private static (string RepositoryRoot, string Revision, string Path) RequireBlob(Url url)
    {
        GitDriveUrl.Parse(url, out var repositoryRoot, out var revision, out var path);
        if (repositoryRoot is null || revision is null || path.Length == 0)
        {
            throw new IOException($"Not a file in a git revision: {url.FullUrl}");
        }

        return (repositoryRoot, revision, path);
    }

    #endregion

    #region Writing: a commit cannot be changed

    private static NotSupportedException ReadOnly() => new("The git history is read-only; copy the file out to change it.");

    public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw ReadOnly();

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw ReadOnly();

    public Task CreateFolderAsync(Url url) => throw ReadOnly();

    public bool DeleteFile(Url url, bool moveToRecycleBin) => throw ReadOnly();

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => throw ReadOnly();

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnly();

    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnly();

    public string GetAvailableDiskSpace() => string.Empty;

    #endregion

    #region Helpers

    /// <summary><c>git -C &lt;repo&gt;</c> rather than a working directory: a repository that has been
    /// deleted then fails with git's own message instead of a process-start error.</summary>
    private Task<ProcessOutput> RunGitAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] arguments) =>
        processRunner.RunAsync("git", ["-C", repositoryRoot, .. arguments], cancellationToken: cancellationToken);

    private static IEnumerable<string> Lines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static bool TrySplit(string line, int count, out string[] fields)
    {
        fields = line.Split('\t', count);
        return fields.Length == count;
    }

    private static DateTime? FromUnix(string seconds) =>
        long.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime
            : null;

    #endregion
}

/// <summary>The <c>git://</c> drive's back-end. See <see cref="GitDriveService"/>.</summary>
public interface IGitDriveService : IEntryListingFileSystem
{
    /// <summary>The repositories listed at the drive root, most recently opened first.</summary>
    IReadOnlyList<string> Repositories { get; }

    /// <summary>Puts <paramref name="repositoryRoot"/> at the top of <see cref="Repositories"/>.</summary>
    void RememberRepository(string repositoryRoot);

    /// <summary>Lists any level of the drive, without its <c>..</c> row.</summary>
    new Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken);
}
