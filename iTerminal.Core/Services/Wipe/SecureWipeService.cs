using iConsole.Helper;
using System.Security.Cryptography;

namespace iTerminal.Core.Services.Wipe;

/// <summary>How many times, and with what, a file's bytes are overwritten before it is deleted.</summary>
public enum WipePattern
{
    /// <summary>One pass of random bytes. Enough for any modern disk; the rest are for policy.</summary>
    Random,

    /// <summary>Zeros, ones, random — the three-pass scheme DoD 5220.22-M names.</summary>
    ThreePass,

    /// <summary>Seven passes alternating fixed bytes and random ones.</summary>
    SevenPass,
}

/// <summary>Where a wipe is: bytes written out of bytes planned, and the file being overwritten.</summary>
public readonly record struct WipeProgress(long BytesDone, long BytesTotal, string CurrentFile);

/// <summary>What a wipe did. <see cref="Failures"/> is one sentence per entry that could not be wiped; the rest went on.</summary>
public sealed record WipeResult(int Files, int Folders, long Bytes, IReadOnlyList<string> Failures, bool Cancelled)
{
    public bool Success => Failures.Count == 0 && !Cancelled;
}

/// <summary>
/// Overwrites files and folders on the local disk and deletes them, so the bytes are not left in
/// the free space. Async by contract: overwriting a large file is as slow as writing it, per pass.
/// </summary>
public interface ISecureWipeService
{
    /// <summary>Adds up what a wipe of <paramref name="paths"/> would write: the files' bytes times the passes. Links count as nothing.</summary>
    long Measure(IEnumerable<string> paths, WipePattern pattern);

    Task<WipeResult> WipeAsync(
        IReadOnlyList<string> paths,
        WipePattern pattern,
        IProgress<WipeProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The wipe, in place.
///
/// <list type="bullet">
/// <item><b>A file is overwritten through a handle opened with write-through</b>, each pass flushed
/// to the device (<c>Flush(true)</c>) before the next starts, then truncated, renamed to a random
/// name (so the old name does not survive in the directory) and deleted.</item>
/// <item><b>A link is deleted, never followed.</b> A symbolic link or junction is a pointer: wiping
/// "through" it would destroy a file or folder somewhere else on the disk, and a folder's wipe walks
/// children, so a folder link is the one entry it must not descend into. Same rule as the delete.</item>
/// <item><b>A failed entry does not stop the others</b>; the failures are listed.</item>
/// <item><b>What it cannot promise.</b> A copy-on-write or journaling file system (APFS, Btrfs, ZFS,
/// NTFS with shadow copies), an SSD that remaps blocks, a snapshot or a backup all keep bytes that an
/// overwrite never reaches. The dialog says so; this class does what an overwrite can do.</item>
/// </list>
/// </summary>
public sealed class SecureWipeService : ISecureWipeService
{
    private const int BufferSize = 1 << 16;

    public long Measure(IEnumerable<string> paths, WipePattern pattern)
    {
        long total = 0;
        foreach (var path in paths)
        {
            foreach (var file in EnumerateFiles(path))
            {
                try
                {
                    total += new FileInfo(file).Length * PassesOf(pattern).Count;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Counted as nothing; the wipe itself reports it.
                }
            }
        }

        return total;
    }

    public Task<WipeResult> WipeAsync(
        IReadOnlyList<string> paths,
        WipePattern pattern,
        IProgress<WipeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Disk work with nothing to await: on a task of its own so the caller's thread is free.
        return Task.Run(() => Wipe(paths, pattern, progress, cancellationToken), CancellationToken.None);
    }

    private WipeResult Wipe(IReadOnlyList<string> paths, WipePattern pattern, IProgress<WipeProgress>? progress, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        var files = 0;
        var folders = 0;
        long bytes = 0;
        var total = Measure(paths, pattern);
        long done = 0;

        void Report(string current) => progress?.Report(new WipeProgress(done, total, current));

        try
        {
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    WipeEntry(path);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    failures.Add($"{Path.GetFileName(path.TrimEnd('/', '\\'))}: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new WipeResult(files, folders, bytes, failures, Cancelled: true);
        }

        return new WipeResult(files, folders, bytes, failures, Cancelled: false);

        void WipeEntry(string path)
        {
            if (FileLinkHelper.IsLink(path))
            {
                DeleteLink(path);
                files++;
                return;
            }

            if (Directory.Exists(path))
            {
                WipeFolder(path);
                return;
            }

            if (File.Exists(path))
            {
                WipeFile(path);
                return;
            }

            throw new FileNotFoundException("it is no longer there");
        }

        void WipeFolder(string folder)
        {
            // Children first, each on its own: one locked file costs that file, not the folder's rest.
            var left = false;
            foreach (var child in Directory.EnumerateFileSystemEntries(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    WipeEntry(child);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    left = true;
                    failures.Add($"{Path.GetFileName(child)}: {exception.Message}");
                }
            }

            if (left)
            {
                return;
            }

            var anonymous = RenameToRandom(folder);
            Directory.Delete(anonymous, recursive: false);
            folders++;
        }

        void WipeFile(string file)
        {
            var info = new FileInfo(file);
            if (info.IsReadOnly)
            {
                info.IsReadOnly = false;
            }

            var length = info.Length;
            var passes = PassesOf(pattern);
            Report(file);
            using (var stream = new FileStream(
                file, FileMode.Open, FileAccess.Write, FileShare.None, BufferSize, FileOptions.WriteThrough))
            {
                var buffer = new byte[BufferSize];
                foreach (var fill in passes)
                {
                    stream.Position = 0;
                    var remaining = length;
                    while (remaining > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var count = (int)Math.Min(buffer.Length, remaining);
                        Fill(buffer.AsSpan(0, count), fill);
                        stream.Write(buffer, 0, count);
                        remaining -= count;
                        done += count;
                        Report(file);
                    }

                    // To the device before the next pass: a cache that merges two passes wiped nothing.
                    stream.Flush(flushToDisk: true);
                }

                stream.SetLength(0);
                stream.Flush(flushToDisk: true);
            }

            var anonymous = RenameToRandom(file);
            File.Delete(anonymous);
            files++;
            bytes += length;
        }
    }

    /// <summary>A link is a pointer: removing it touches nothing it points at.</summary>
    private static void DeleteLink(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: false);
        }
        else
        {
            File.Delete(path);
        }
    }

    /// <summary>Moves the entry to a random name in its own folder, so the name is not left in the directory. Returns the new path.</summary>
    private static string RenameToRandom(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed);
        if (parent is null)
        {
            return trimmed;
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var target = Path.Combine(parent, Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant());
            if (File.Exists(target) || Directory.Exists(target))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(trimmed))
                {
                    Directory.Move(trimmed, target);
                }
                else
                {
                    File.Move(trimmed, target);
                }

                return target;
            }
            catch (IOException)
            {
                // A name taken in the meantime, or a file system that will not rename it: try again, then give up quietly.
            }
        }

        return trimmed;
    }

    /// <summary>What each pass writes: a byte value, or null for random.</summary>
    internal static IReadOnlyList<byte?> PassesOf(WipePattern pattern) => pattern switch
    {
        WipePattern.ThreePass => [0x00, 0xFF, null],
        WipePattern.SevenPass => [0x00, 0xFF, null, 0x55, 0xAA, null, 0x00],
        _ => [null],
    };

    private static void Fill(Span<byte> buffer, byte? value)
    {
        if (value is { } fixedByte)
        {
            buffer.Fill(fixedByte);
        }
        else
        {
            RandomNumberGenerator.Fill(buffer);
        }
    }

    /// <summary>The files under <paramref name="path"/> a wipe would overwrite — through no link.</summary>
    private static IEnumerable<string> EnumerateFiles(string path)
    {
        if (FileLinkHelper.IsLink(path))
        {
            yield break;
        }

        if (File.Exists(path))
        {
            yield return path;
            yield break;
        }

        if (!Directory.Exists(path))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(folder).ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (FileLinkHelper.IsLink(child))
                {
                    continue;
                }

                if (Directory.Exists(child))
                {
                    pending.Push(child);
                }
                else
                {
                    yield return child;
                }
            }
        }
    }
}
