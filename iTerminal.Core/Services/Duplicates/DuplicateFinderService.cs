using System.Security.Cryptography;

namespace iTerminal.Core.Services.Duplicates;

/// <summary>One of several files with the same content.</summary>
public sealed record DuplicateFile(string Path, DateTime Modified)
{
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>Files that hold exactly the same bytes: <see cref="Size"/> each, so removing all but one frees <see cref="Wasted"/>.</summary>
public sealed record DuplicateGroup(long Size, IReadOnlyList<DuplicateFile> Files)
{
    public long Wasted => Size * Math.Max(0, Files.Count - 1);

    /// <summary>The same group without <paramref name="removed"/>; null once fewer than two copies are left (nothing is duplicated any more).</summary>
    public DuplicateGroup? Without(IReadOnlySet<string> removed)
    {
        var rest = Files.Where(f => !removed.Contains(f.Path)).ToList();
        return rest.Count >= 2 ? this with { Files = rest } : null;
    }
}

/// <summary>Where a scan is: each stage narrows the candidates for the next, which is why the last is the only one that reads whole files.</summary>
public enum DuplicateScanPhase
{
    /// <summary>Walking the folders, noting every file's size.</summary>
    Listing,

    /// <summary>Reading the first few kilobytes of files that share a size.</summary>
    Sampling,

    /// <summary>Hashing the whole of the files that still look alike.</summary>
    Hashing,

    Done,
}

/// <summary>A scan in progress, or finished: counters a dialog can read while it runs.</summary>
public sealed class DuplicateScan
{
    private readonly CancellationTokenSource _cancellation = new();
    private long _files;
    private long _hashedBytes;
    private long _bytesToHash;
    private volatile string _current = string.Empty;
    private volatile DuplicateScanPhase _phase = DuplicateScanPhase.Listing;

    internal DuplicateScan(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public DuplicateScanPhase Phase => _phase;

    public long FilesScanned => Interlocked.Read(ref _files);

    /// <summary>How many bytes the hashing stage has to read in all (known once it starts), and how many it has read.</summary>
    public long BytesToHash => Interlocked.Read(ref _bytesToHash);

    public long BytesHashed => Interlocked.Read(ref _hashedBytes);

    /// <summary>The folder or file being looked at now.</summary>
    public string Current => _current;

    /// <summary>The finished result, largest waste first; empty until the scan ends, and after it was cancelled or failed.</summary>
    public IReadOnlyList<DuplicateGroup> Groups { get; private set; } = [];

    public bool IsComplete { get; private set; }

    public bool WasCancelled => _cancellation.IsCancellationRequested;

    /// <summary>Completes when the scan ends — also when it was cancelled.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Cancel() => _cancellation.Cancel();

    internal void Run(Func<DuplicateScan, CancellationToken, IReadOnlyList<DuplicateGroup>> work)
    {
        Completion = Task.Run(() =>
        {
            try
            {
                Groups = work(this, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                Groups = [];
            }
            finally
            {
                _phase = DuplicateScanPhase.Done;
                IsComplete = true;
            }
        });
    }

    internal void SetPhase(DuplicateScanPhase phase) => _phase = phase;

    internal void SetCurrent(string current) => _current = current;

    internal void CountFile() => Interlocked.Increment(ref _files);

    internal void AddBytesToHash(long bytes) => Interlocked.Add(ref _bytesToHash, bytes);

    internal void AddHashed(long bytes) => Interlocked.Add(ref _hashedBytes, bytes);
}

/// <summary>Looks for files with identical content under a folder.</summary>
public interface IDuplicateFinderService
{
    /// <summary>Starts a scan of <paramref name="path"/> on a background task and returns at once.</summary>
    DuplicateScan Start(string path);
}

/// <summary>
/// Finds duplicate files by size, then a sample, then the whole content.
///
/// <para><b>Three narrowing stages, so most files are never read.</b> Two files can only be equal if
/// they are the same size — and on a real disk that alone rules out nearly everything, for the price
/// of a directory listing. What shares a size is compared by its first <see cref="SampleBytes"/> (most
/// non-duplicates of the same size, such as two photos or two logs, already differ there), and only
/// what still agrees is read to the end and hashed with SHA-256. A hash match is trusted as equality;
/// a whole-file comparison would double the reading for a collision that cannot be found by accident.</para>
///
/// <para><b>What is not looked at:</b> empty files (every one is a "duplicate" of every other, and
/// removing them frees nothing), links (a pointer is not a copy, and a junction loop never ends), and
/// the internals of <c>.git</c>, <c>.svn</c> and <c>.hg</c> (an object store is full of near-copies
/// that must not be touched). A folder or file that cannot be read is skipped, not fatal. Hard links
/// to one file are listed as copies — they are, as far as the directory goes — and deleting one of
/// them frees nothing; the dialog shows sizes, not what would really come back.</para>
/// </summary>
public sealed class DuplicateFinderService : IDuplicateFinderService
{
    /// <summary>How much of the start of a file the second stage compares.</summary>
    public const int SampleBytes = 4096;

    private static readonly string[] SkippedFolders = [".git", ".svn", ".hg"];

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.None,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    public DuplicateScan Start(string path)
    {
        var scan = new DuplicateScan(path);
        scan.Run((s, token) => Find(s, path, token));
        return scan;
    }

    internal static IReadOnlyList<DuplicateGroup> Find(DuplicateScan scan, string path, CancellationToken cancellationToken)
    {
        var bySize = new Dictionary<long, List<FileInfo>>();
        Walk(scan, new DirectoryInfo(path), bySize, cancellationToken);

        scan.SetPhase(DuplicateScanPhase.Sampling);
        var candidates = bySize.Where(pair => pair.Value.Count > 1).ToList();
        var sampled = new List<(long Size, List<FileInfo> Files)>();
        foreach (var (size, files) in candidates)
        {
            var groups = new Dictionary<string, List<FileInfo>>();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scan.SetCurrent(file.FullName);
                if (TrySample(file, size, out var key))
                {
                    if (!groups.TryGetValue(key, out var list))
                    {
                        groups[key] = list = [];
                    }

                    list.Add(file);
                }
            }

            sampled.AddRange(groups.Values.Where(g => g.Count > 1).Select(g => (size, g)));
        }

        scan.SetPhase(DuplicateScanPhase.Hashing);
        scan.AddBytesToHash(sampled.Sum(g => g.Size * g.Files.Count));
        var result = new List<DuplicateGroup>();
        foreach (var (size, files) in sampled)
        {
            var byHash = new Dictionary<string, List<FileInfo>>();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scan.SetCurrent(file.FullName);
                if (TryHash(scan, file, cancellationToken, out var hash))
                {
                    if (!byHash.TryGetValue(hash, out var list))
                    {
                        byHash[hash] = list = [];
                    }

                    list.Add(file);
                }
            }

            foreach (var same in byHash.Values.Where(g => g.Count > 1))
            {
                result.Add(new DuplicateGroup(
                    size,
                    [.. same.OrderBy(f => f.LastWriteTime).ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                        .Select(f => new DuplicateFile(f.FullName, f.LastWriteTime))]));
            }
        }

        return [.. result.OrderByDescending(g => g.Wasted).ThenBy(g => g.Files[0].Path, StringComparer.OrdinalIgnoreCase)];
    }

    private static void Walk(DuplicateScan scan, DirectoryInfo folder, Dictionary<long, List<FileInfo>> bySize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        scan.SetCurrent(folder.FullName);
        try
        {
            foreach (var entry in folder.EnumerateFileSystemInfos("*", Options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (entry is DirectoryInfo directory)
                {
                    if (!SkippedFolders.Contains(directory.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        Walk(scan, directory, bySize, cancellationToken);
                    }
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
                        continue;
                    }

                    scan.CountFile();
                    if (length <= 0)
                    {
                        continue;
                    }

                    if (!bySize.TryGetValue(length, out var list))
                    {
                        bySize[length] = list = [];
                    }

                    list.Add(file);
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // A folder that cannot be read has no duplicates to offer.
        }
    }

    private static bool TrySample(FileInfo file, long size, out string key)
    {
        key = string.Empty;
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            var buffer = new byte[(int)Math.Min(size, SampleBytes)];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                {
                    return false;   // shorter than its listing said: it changed under us
                }

                read += n;
            }

            key = Convert.ToHexString(SHA256.HashData(buffer));
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool TryHash(DuplicateScan scan, FileInfo file, CancellationToken cancellationToken, out string hash)
    {
        hash = string.Empty;
        try
        {
            using var algorithm = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.SequentialScan);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                algorithm.AppendData(buffer, 0, read);
                scan.AddHashed(read);
            }

            hash = Convert.ToHexString(algorithm.GetHashAndReset());
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
