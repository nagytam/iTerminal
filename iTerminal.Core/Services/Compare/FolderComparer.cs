using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Tasks;
using System.Globalization;
using System.Security.Cryptography;

namespace iTerminal.Core.Services.Compare;

/// <summary>How two files with the same name are judged the same.</summary>
public enum CompareMode
{
    /// <summary>Same size and the same modification time (within the drives' resolution): fast, and right almost always.</summary>
    SizeAndDate,

    /// <summary>Same size, whatever the dates — for copies whose dates were not kept.</summary>
    SizeOnly,

    /// <summary>Same bytes: both files are read. The answer that cannot be wrong, at the price of reading everything.</summary>
    Content,
}

/// <summary>What the comparison found for one name.</summary>
public enum DiffKind
{
    Identical,
    LeftOnly,
    RightOnly,
    LeftNewer,
    RightNewer,

    /// <summary>They differ, and nothing says which side is the newer one (same date, a file against a folder, changes both ways in a folder).</summary>
    Different,
}

public sealed record CompareOptions(CompareMode Mode = CompareMode.SizeAndDate, bool IncludeSubfolders = false);

/// <summary>One name found on either side.</summary>
/// <param name="Path">'/'-separated, relative to the compared folders.</param>
public sealed record DiffItem(string Path, bool IsFolder, DiffKind Kind, Url? Left, Url? Right, long? LeftSize, long? RightSize, DateTime? LeftTime, DateTime? RightTime)
{
    public bool IsTopLevel => !Path.Contains('/');

    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
}

public sealed record CompareResult(IReadOnlyList<DiffItem> Items)
{
    public IEnumerable<DiffItem> Differences => Items.Where(i => i.Kind != DiffKind.Identical);

    public int Count(DiffKind kind) => Items.Count(i => i.Kind == kind && !i.IsFolder);

    /// <summary>"3 only on the left, 1 newer on the right, 12 identical" — files only; a folder is the sum of its files.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        void Add(DiffKind kind, string text)
        {
            var count = Count(kind);
            if (count > 0)
            {
                parts.Add($"{count} {text}");
            }
        }

        Add(DiffKind.LeftOnly, "only on the left");
        Add(DiffKind.RightOnly, "only on the right");
        Add(DiffKind.LeftNewer, "newer on the left");
        Add(DiffKind.RightNewer, "newer on the right");
        Add(DiffKind.Different, "different");
        var folders = Items.Count(i => i.IsFolder && i.Kind is DiffKind.LeftOnly or DiffKind.RightOnly);
        if (folders > 0)
        {
            parts.Add($"{folders} folder{(folders == 1 ? string.Empty : "s")} on one side only");
        }

        Add(DiffKind.Identical, "identical");
        return parts.Count == 0 ? "Both folders are empty." : string.Join(", ", parts) + ".";
    }
}

/// <summary>
/// Shift+F2: the two panels' folders side by side, name by name.
///
/// <para>Each side is listed through its own drive — a local folder by <see cref="DirectoryInfo"/>, the
/// drives that know sizes and dates through <see cref="IEntryListingFileSystem"/>, the rest through
/// the same walker F5 uses — so any two can be compared: a folder against a ZIP, a commit against
/// the working copy, a container against the disk.</para>
///
/// <para><b>Dates are compared at the coarser side's resolution.</b> Local files carry the time to
/// the second, but a ZIP stores it to two seconds and most other listings (ls, the URL strings) to
/// the minute, and a copy that kept its date would otherwise show up as "newer" over a rounding.
/// Two local files must agree within 2 s; anything else within the minute. When that is too coarse,
/// <see cref="CompareMode.Content"/> reads the bytes.</para>
///
/// <para>A folder present on both sides is only looked into with
/// <see cref="CompareOptions.IncludeSubfolders"/>, and then stands for what is inside it: newer on
/// the left when every difference inside points left, <see cref="DiffKind.Different"/> when they
/// point both ways.</para>
/// </summary>
public static class FolderComparer
{
    private static readonly TimeSpan PreciseTolerance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CoarseTolerance = TimeSpan.FromSeconds(60);

    private static StringComparer NameComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>One child of a compared folder, with what its drive could say about it.</summary>
    internal sealed record Node(string Name, Url Url, bool IsFolder, long? Size, DateTime? Time, bool PreciseTime);

    public static async Task<CompareResult> CompareAsync(
        Url leftFolder,
        IFileSystem left,
        Url rightFolder,
        IFileSystem right,
        CompareOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var items = new List<DiffItem>();
        await CompareFolderAsync(string.Empty, leftFolder, left, rightFolder, right, options, items, progress, cancellationToken);
        return new CompareResult(items);
    }

    /// <summary>Compares one level, appending its items (a folder before its contents); returns what the level amounts to.</summary>
    private static async Task<DiffKind> CompareFolderAsync(
        string prefix, Url leftFolder, IFileSystem left, Url rightFolder, IFileSystem right,
        CompareOptions options, List<DiffItem> items, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(prefix.Length == 0 ? "/" : prefix);
        var leftNodes = (await ListAsync(leftFolder, left, cancellationToken)).ToDictionary(n => n.Name, NameComparer);
        var rightNodes = (await ListAsync(rightFolder, right, cancellationToken)).ToDictionary(n => n.Name, NameComparer);
        var names = leftNodes.Keys.Union(rightNodes.Keys, NameComparer)
            .OrderBy(name => (leftNodes.GetValueOrDefault(name) ?? rightNodes[name]).IsFolder ? 0 : 1)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var kinds = new List<DiffKind>();
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            leftNodes.TryGetValue(name, out var l);
            rightNodes.TryGetValue(name, out var r);
            var path = prefix + (l ?? r)!.Name;

            if (l is null || r is null)
            {
                var only = (l ?? r)!;
                var kind = l is null ? DiffKind.RightOnly : DiffKind.LeftOnly;
                items.Add(Item(path, only.IsFolder, kind, l, r));
                kinds.Add(kind);
                continue;
            }

            if (l.IsFolder != r.IsFolder)
            {
                items.Add(Item(path, l.IsFolder, DiffKind.Different, l, r));
                kinds.Add(DiffKind.Different);
                continue;
            }

            if (l.IsFolder && ContentModelFolderTaskHelper.IsLinkBackToAncestor(l.Url))
            {
                // Listed by nothing: descending into it would compare the same folders for ever.
                continue;
            }

            if (l.IsFolder)
            {
                if (!options.IncludeSubfolders)
                {
                    items.Add(Item(path, true, DiffKind.Identical, l, r));
                    continue;
                }

                // The folder's row goes before its contents, but its verdict is only known after them.
                var index = items.Count;
                items.Add(Item(path, true, DiffKind.Identical, l, r));
                var folderKind = await CompareFolderAsync(path + "/", l.Url, left, r.Url, right, options, items, progress, cancellationToken);
                items[index] = items[index] with { Kind = folderKind };
                kinds.Add(folderKind);
                continue;
            }

            var fileKind = await CompareFilesAsync(l, left, r, right, options.Mode, cancellationToken);
            items.Add(Item(path, false, fileKind, l, r));
            kinds.Add(fileKind);
        }

        return Aggregate(kinds);
    }

    private static DiffItem Item(string path, bool isFolder, DiffKind kind, Node? l, Node? r) =>
        new(path, isFolder, kind, l?.Url, r?.Url, l?.Size, r?.Size, l?.Time, r?.Time);

    /// <summary>What a folder amounts to, from what is inside it.</summary>
    internal static DiffKind Aggregate(IEnumerable<DiffKind> kinds)
    {
        var differences = kinds.Where(k => k != DiffKind.Identical).ToList();
        if (differences.Count == 0)
        {
            return DiffKind.Identical;
        }

        if (differences.All(k => k is DiffKind.LeftOnly or DiffKind.LeftNewer))
        {
            return DiffKind.LeftNewer;
        }

        return differences.All(k => k is DiffKind.RightOnly or DiffKind.RightNewer) ? DiffKind.RightNewer : DiffKind.Different;
    }

    private static async Task<DiffKind> CompareFilesAsync(Node l, IFileSystem left, Node r, IFileSystem right, CompareMode mode, CancellationToken cancellationToken)
    {
        var newer = Newer(l, r);
        var sameSize = l.Size is { } ls && r.Size is { } rs ? ls == rs : (bool?)null;

        switch (mode)
        {
            case CompareMode.SizeOnly:
                return sameSize != false ? DiffKind.Identical : newer ?? DiffKind.Different;

            case CompareMode.Content:
                if (sameSize == false)
                {
                    return newer ?? DiffKind.Different;
                }

                var leftHash = await HashAsync(l.Url, left, cancellationToken);
                var rightHash = await HashAsync(r.Url, right, cancellationToken);
                var sameBytes = leftHash.AsSpan().SequenceEqual(rightHash);
                return sameBytes ? DiffKind.Identical : newer ?? DiffKind.Different;

            default:
                if (sameSize != false && newer is null)
                {
                    return DiffKind.Identical;
                }

                return newer ?? DiffKind.Different;
        }
    }

    /// <summary>The side whose file is newer beyond both sides' resolution, or null when neither is (or a date is unknown).</summary>
    internal static DiffKind? Newer(Node l, Node r)
    {
        if (l.Time is not { } leftTime || r.Time is not { } rightTime)
        {
            return null;
        }

        var tolerance = l.PreciseTime && r.PreciseTime ? PreciseTolerance : CoarseTolerance;
        if (!l.PreciseTime || !r.PreciseTime)
        {
            leftTime = Truncate(leftTime);
            rightTime = Truncate(rightTime);
        }

        var difference = leftTime - rightTime;
        return difference > tolerance ? DiffKind.LeftNewer
            : difference < -tolerance ? DiffKind.RightNewer
            : null;
    }

    private static DateTime Truncate(DateTime time) => new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);

    #region Listing
    internal static async Task<List<Node>> ListAsync(Url folder, IFileSystem fileSystem, CancellationToken cancellationToken)
    {
        if (folder.ContainerType == ContainerType.FileSystem)
        {
            return await Task.Run(() => ListLocal(folder), cancellationToken);
        }

        if (fileSystem is IEntryListingFileSystem entryListing)
        {
            var entries = await entryListing.ListAsync(folder, cancellationToken);
            return [.. entries
                .Where(e => !FocusedEntry.IsParentEntry(e))
                .Select(e => new Node(
                    FocusedEntry.NameOf(e.Url),
                    e.Url,
                    e.Url.PathType == PathType.Folder,
                    e is VirtualDriveEntry v ? v.Size : e.Url.Length,
                    e is VirtualDriveEntry { Modified: { } modified } ? modified : ParseTime(e.Url),
                    PreciseTime: false))];
        }

        return await Task.Run(() => ContentModelFolderTaskHelper.ListChildren(fileSystem, folder)
            .Select(url => new Node(FocusedEntry.NameOf(url), url, url.PathType == PathType.Folder,
                url.PathType == PathType.Folder ? null : url.Length, ParseTime(url), PreciseTime: false))
            .ToList(), cancellationToken);
    }

    private static List<Node> ListLocal(Url folder)
    {
        var nodes = new List<Node>();
        foreach (var info in new DirectoryInfo(folder.FullName).EnumerateFileSystemInfos())
        {
            var isFolder = info is DirectoryInfo;
            var url = new Url(info.FullName + (isFolder ? Path.DirectorySeparatorChar.ToString() : string.Empty));
            nodes.Add(new Node(info.Name, url, isFolder, isFolder ? null : ((FileInfo)info).Length, info.LastWriteTime, PreciseTime: true));
        }

        return nodes;
    }

    /// <summary>The date a listing put in the URL ("2024-05-06" + "07:08"), to the minute.</summary>
    private static DateTime? ParseTime(Url url) =>
        DateTime.TryParse($"{url.Date} {url.Time}".Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time)
            ? time
            : null;
    #endregion

    #region Content
    private static async Task<byte[]> HashAsync(Url url, IFileSystem fileSystem, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (url.ContainerType == ContainerType.FileSystem)
        {
            await using var file = new FileStream(url.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true);
            var buffer = new byte[81920];
            int read;
            while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }
        else
        {
            await fileSystem.StreamToAsync(url, new HashingStream(hash), null, cancellationToken);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>A write-only stream that feeds what is written to it into a hash — for drives that can only stream a file out.</summary>
    private sealed class HashingStream(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            hash.AppendData(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            hash.AppendData(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    #endregion
}
