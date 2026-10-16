namespace iTerminal.Core.Services.DiskUsage;

/// <summary>
/// One folder or file in a disk-usage tree. A folder's <see cref="Size"/>, <see cref="FileCount"/> and
/// <see cref="FolderCount"/> are totals for everything beneath it; the children are sorted by size,
/// largest first. A folder keeps its <see cref="DiskUsageScan.FilesKeptPerFolder"/> largest files as
/// nodes and folds the rest into one <see cref="IsOther"/> node, so the tree of a disk with millions of
/// files stays a few hundred thousand objects.
/// </summary>
public sealed class DiskUsageNode(string name, string path, bool isFolder, DiskUsageNode? parent)
{
    public string Name { get; } = name;

    public string Path { get; } = path;

    public bool IsFolder { get; } = isFolder;

    /// <summary>The "(n more files)" row: stands for several files that were not worth a row each.</summary>
    public bool IsOther { get; init; }

    public DiskUsageNode? Parent { get; } = parent;

    public long Size { get; internal set; }

    /// <summary>Files at or below this node (1 for a file).</summary>
    public long FileCount { get; internal set; }

    /// <summary>Folders below this node, not counting itself.</summary>
    public long FolderCount { get; internal set; }

    /// <summary>The folder could not be (fully) read — no permission, or it vanished mid-scan.</summary>
    public bool Inaccessible { get; internal set; }

    public IReadOnlyList<DiskUsageNode> Children { get; internal set; } = [];
}

/// <summary>A scan in progress, or finished: counters a dialog can read while it runs.</summary>
public sealed class DiskUsageScan
{
    /// <summary>How many files a folder keeps as rows of their own; the rest are one "more files" row.</summary>
    public const int FilesKeptPerFolder = 40;

    private readonly CancellationTokenSource _cancellation = new();
    private long _files;
    private long _bytes;
    private volatile string _current = string.Empty;

    internal DiskUsageScan(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public long FilesScanned => Interlocked.Read(ref _files);

    public long BytesScanned => Interlocked.Read(ref _bytes);

    /// <summary>The folder being read now.</summary>
    public string CurrentFolder => _current;

    /// <summary>The finished tree; null until the scan ends, and after it was cancelled or failed.</summary>
    public DiskUsageNode? Root { get; private set; }

    public bool IsComplete { get; private set; }

    public bool WasCancelled => _cancellation.IsCancellationRequested;

    /// <summary>Completes when the scan ends — also when it was cancelled.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Cancel() => _cancellation.Cancel();

    internal void Run(Func<string, DiskUsageNode?, Func<string, string>, CancellationToken, DiskUsageNode> walk)
    {
        Completion = Task.Run(() =>
        {
            try
            {
                Root = walk(Path, null, Visit, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                Root = null;
            }
            finally
            {
                IsComplete = true;
            }
        });
    }

    private string Visit(string folder)
    {
        _current = folder;
        return folder;
    }

    internal void CountFile(long size)
    {
        Interlocked.Increment(ref _files);
        Interlocked.Add(ref _bytes, size);
    }
}

/// <summary>Measures where the space went under a folder.</summary>
public interface IDiskUsageService
{
    /// <summary>Starts walking <paramref name="path"/> on a background task and returns at once.</summary>
    DiskUsageScan Start(string path);
}

/// <summary>
/// The walk behind the disk-usage view. It reads the disk itself and nothing else: a link is counted
/// as the entry it is (never followed — a junction loop would never end, and the space is not the
/// link's), hidden and system files are included (they take space), and a folder it may not read is
/// kept as a row marked inaccessible with what could be seen. Sizes are file lengths, not the
/// allocated clusters: that is what every other size in the app shows.
/// </summary>
public sealed class DiskUsageService : IDiskUsageService
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.None,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    public DiskUsageScan Start(string path)
    {
        var scan = new DiskUsageScan(path);
        scan.Run((root, parent, visit, token) => Walk(scan, root, parent, visit, token));
        return scan;
    }

    private static DiskUsageNode Walk(
        DiskUsageScan scan,
        string path,
        DiskUsageNode? parent,
        Func<string, string> visit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        var node = new DiskUsageNode(name.Length == 0 ? path : name, path, isFolder: true, parent);
        visit(path);

        var folders = new List<DiskUsageNode>();
        var files = new List<(string Name, long Size)>();
        long folderCount = 0;
        try
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", Options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // A link is a pointer: its own size is nothing worth a row, and following it
                    // double-counts or loops.
                    continue;
                }

                if (entry is DirectoryInfo directory)
                {
                    var child = Walk(scan, directory.FullName, node, visit, cancellationToken);
                    folders.Add(child);
                    folderCount += 1 + child.FolderCount;
                    node.Size += child.Size;
                    node.FileCount += child.FileCount;
                }
                else if (entry is FileInfo file)
                {
                    long length;
                    try
                    {
                        length = file.Length;
                    }
                    catch (IOException)
                    {
                        node.Inaccessible = true;
                        continue;
                    }

                    files.Add((file.Name, length));
                    node.Size += length;
                    node.FileCount++;
                    scan.CountFile(length);
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            node.Inaccessible = true;
        }

        node.FolderCount = folderCount;

        var children = new List<DiskUsageNode>(folders);
        files.Sort((a, b) => b.Size.CompareTo(a.Size));
        foreach (var (fileName, size) in files.Take(DiskUsageScan.FilesKeptPerFolder))
        {
            children.Add(new DiskUsageNode(fileName, System.IO.Path.Combine(path, fileName), isFolder: false, node)
            {
                Size = size,
                FileCount = 1,
            });
        }

        if (files.Count > DiskUsageScan.FilesKeptPerFolder)
        {
            var rest = files.Skip(DiskUsageScan.FilesKeptPerFolder).ToList();
            children.Add(new DiskUsageNode($"({rest.Count:N0} more files)", path, isFolder: false, node)
            {
                IsOther = true,
                Size = rest.Sum(f => f.Size),
                FileCount = rest.Count,
            });
        }

        children.Sort((a, b) => b.Size.CompareTo(a.Size));
        node.Children = children;
        return node;
    }
}
