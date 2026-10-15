using System.IO.Enumeration;
using System.Text;

namespace iTerminal.Core.Services.Search;

/// <summary>What F7 searches for.</summary>
/// <param name="StartFolder">The local folder the walk starts in.</param>
/// <param name="NamePatterns">
/// File-name patterns separated by <c>;</c> or <c>,</c> — <c>*.cs;*.md</c>. A pattern without a
/// wildcard matches anywhere in the name (<c>report</c> finds <c>2024-report-final.pdf</c>), which is
/// what someone typing a word means. Empty matches every name.
/// </param>
/// <param name="ContainingText">Text the file must contain, case-insensitively; empty to match on names only.</param>
/// <param name="IncludeSubfolders">Whether the walk descends below <paramref name="StartFolder"/>.</param>
public sealed record FileSearchQuery(
    string StartFolder,
    string NamePatterns,
    string ContainingText = "",
    bool IncludeSubfolders = true)
{
    public bool SearchesContent => ContainingText.Length > 0;

    /// <summary>The patterns as the matcher uses them: wildcard-free words wrapped in <c>*…*</c>.</summary>
    internal IReadOnlyList<string> Expressions => FileSearch.ParsePatterns(NamePatterns);
}

/// <summary>One file (or folder) the search found.</summary>
/// <param name="LineNumber">For a content search, the 1-based line of the first match; 0 otherwise.</param>
/// <param name="LineText">That line, trimmed and shortened — what the result row shows as the reason it matched.</param>
public sealed record FileSearchHit(
    string Path,
    bool IsFolder,
    long Size,
    DateTime Modified,
    int LineNumber = 0,
    string LineText = "");

/// <summary>A snapshot of a running search, for the status line.</summary>
public sealed record FileSearchProgress(int FoldersScanned, int FilesScanned, int Hits, string CurrentFolder);

/// <summary>
/// The walk behind F7: local folders, by name and optionally by content.
///
/// <para>The tree is walked with <see cref="FileSystemEnumerable{TResult}"/> rather than
/// <c>Directory.EnumerateFiles</c> for two things the convenience API cannot do: skip a folder
/// that is a reparse point (a junction back up the tree would otherwise make the walk endless, and
/// Windows ships several under the user profile) while still looking at reparse-point <i>files</i>
/// (every OneDrive placeholder is one), and stay inside version-control internals' doors —
/// <c>.git</c> holds thousands of objects whose names nobody searches for.</para>
///
/// <para>Content is matched line by line from a stream, never by reading the file whole, and a file
/// with a NUL byte in its first block is treated as binary and skipped: searching an executable
/// for a word finds nothing a person wants and costs the most time.</para>
/// </summary>
public static class FileSearch
{
    /// <summary>Files larger than this are matched by name only.</summary>
    public const long MaxContentSearchBytes = 64L * 1024 * 1024;

    /// <summary>The search stops adding hits past this many; the status line says so.</summary>
    public const int MaxHits = 10_000;

    private const int SnippetLength = 160;

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg",
    };

    /// <summary>
    /// Runs <paramref name="query"/>, handing each hit to <paramref name="onHit"/> as it is found and
    /// a progress snapshot to <paramref name="onProgress"/> at least once per folder. Synchronous and
    /// CPU/disk-bound: run it on a background task. Returns the final progress.
    /// </summary>
    public static FileSearchProgress Run(
        FileSearchQuery query,
        Action<FileSearchHit> onHit,
        Action<FileSearchProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var expressions = query.Expressions;
        var folders = 0;
        var files = 0;
        var hits = 0;
        var currentFolder = query.StartFolder;

        if (!Directory.Exists(query.StartFolder))
        {
            throw new DirectoryNotFoundException($"No such folder: {query.StartFolder}");
        }

        var pending = new Stack<string>();
        pending.Push(query.StartFolder);
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.System,
            ReturnSpecialDirectories = false,
        };

        while (pending.Count > 0 && hits < MaxHits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentFolder = pending.Pop();
            folders++;
            onProgress?.Invoke(new FileSearchProgress(folders, files, hits, currentFolder));

            List<(string Path, bool IsFolder, long Size, DateTime Modified, bool IsLink)> children;
            try
            {
                children = [.. new FileSystemEnumerable<(string, bool, long, DateTime, bool)>(
                    currentFolder,
                    (ref FileSystemEntry entry) => (
                        entry.ToFullPath(),
                        entry.IsDirectory,
                        entry.IsDirectory ? 0 : entry.Length,
                        entry.LastWriteTimeUtc.LocalDateTime,
                        (entry.Attributes & FileAttributes.ReparsePoint) != 0),
                    options)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // Sub-folders are pushed in reverse so they are walked in name order.
            var subFolders = new List<string>();
            foreach (var child in children.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(child.Path);

                if (child.IsFolder)
                {
                    if (query.IncludeSubfolders && !child.IsLink && !SkippedFolders.Contains(name))
                    {
                        subFolders.Add(child.Path);
                    }

                    // A folder can only match a name search: it has no content to contain the text.
                    if (!query.SearchesContent && MatchesName(name, expressions))
                    {
                        onHit(new FileSearchHit(child.Path, true, 0, child.Modified));
                        hits++;
                    }

                    continue;
                }

                files++;
                if (!MatchesName(name, expressions))
                {
                    continue;
                }

                if (!query.SearchesContent)
                {
                    onHit(new FileSearchHit(child.Path, false, child.Size, child.Modified));
                    hits++;
                }
                else if (child.Size <= MaxContentSearchBytes
                         && FindInFile(child.Path, query.ContainingText, cancellationToken) is { } match)
                {
                    onHit(new FileSearchHit(child.Path, false, child.Size, child.Modified, match.LineNumber, match.LineText));
                    hits++;
                }

                if (hits >= MaxHits)
                {
                    break;
                }
            }

            for (var i = subFolders.Count - 1; i >= 0; i--)
            {
                pending.Push(subFolders[i]);
            }
        }

        var final = new FileSearchProgress(folders, files, hits, currentFolder);
        onProgress?.Invoke(final);
        return final;
    }

    /// <summary>
    /// <c>*.cs;*.md</c> split into the expressions <see cref="MatchesName"/> takes: separated by
    /// <c>;</c> or <c>,</c>, a word without wildcards wrapped in <c>*…*</c>, nothing at all meaning
    /// every name. Shared by F7 and by marking rows in a panel, so the two read a pattern alike.
    /// </summary>
    internal static IReadOnlyList<string> ParsePatterns(string? patterns)
    {
        var expressions = (patterns ?? string.Empty)
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.IndexOfAny(['*', '?']) >= 0 ? p : $"*{p}*")
            .ToArray();
        return expressions.Length == 0 ? ["*"] : expressions;
    }

    internal static bool MatchesName(string name, IReadOnlyList<string> expressions)
    {
        foreach (var expression in expressions)
        {
            if (FileSystemName.MatchesSimpleExpression(expression, name, ignoreCase: true))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The first line of <paramref name="path"/> containing <paramref name="text"/>, or null — also
    /// for a binary file and for one that cannot be opened (in use, gone since it was listed).
    /// UTF-8 unless a byte-order mark says otherwise; Latin-1 bytes that are not valid UTF-8 still
    /// match on their ASCII part, which is what a search term almost always is.
    /// </summary>
    internal static (int LineNumber, string LineText)? FindInFile(string path, string text, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            if (LooksBinary(stream))
            {
                return null;
            }

            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var lineNumber = 0;
            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                if ((lineNumber & 0x3FF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var index = line.IndexOf(text, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    return (lineNumber, Snippet(line, index));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static bool LooksBinary(Stream stream)
    {
        Span<byte> head = stackalloc byte[8000];
        var read = stream.Read(head);
        var bytes = head[..read];

        // UTF-16 text is full of NULs and is still text; its byte-order mark says so.
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            return false;
        }

        return bytes.IndexOf((byte)0) >= 0;
    }

    /// <summary>The matching line trimmed, and when long, the part around the match.</summary>
    private static string Snippet(string line, int index)
    {
        var trimmed = line.Trim();
        if (trimmed.Length <= SnippetLength)
        {
            return trimmed;
        }

        var offset = line.Length - line.TrimStart().Length;
        var start = Math.Clamp(index - offset - SnippetLength / 3, 0, trimmed.Length - SnippetLength);
        return (start > 0 ? "…" : string.Empty) + trimmed.Substring(start, SnippetLength) + "…";
    }
}
