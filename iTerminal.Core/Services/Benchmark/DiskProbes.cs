using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace iTerminal.Core.Services.Benchmark;

/// <summary>
/// The disk probes only read. They open an existing file of the program (or of the .NET runtime) with read access and
/// share-read, never create, rename, extend or delete anything, and never change a file's contents. The one thing a read
/// can still do is update the file's access time, which the operating system does for any read.
/// </summary>
internal static class BenchmarkFiles
{
    /// <summary>The smallest file worth reading: smaller ones are answered from memory and say nothing about the disk.</summary>
    internal const long MinimumBytes = 8L * 1024 * 1024;

    /// <summary>The largest file of at least <see cref="MinimumBytes"/> directly inside one of the folders, or null when there is none.</summary>
    internal static FileInfo? Largest(IEnumerable<string> folders)
    {
        FileInfo? best = null;
        foreach (string folder in folders)
        {
            try
            {
                foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true }))
                {
                    if (file.Length >= MinimumBytes && (best is null || file.Length > best.Length))
                    {
                        best = file;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be listed contributes no file.
            }
        }

        return best;
    }

    internal const string NoFileReason = "no file of 8 MB or more in the program folder to read";
}

/// <summary>Reads the largest file of the program in order, from start to end, and reports the bytes a second. Reads are repeated until the target is reached.</summary>
public sealed class DiskSequentialReadProbe : IBenchmarkProbe
{
    internal const long TargetBytes = 256L * 1024 * 1024;
    private const int BufferBytes = 1024 * 1024;

    private readonly string[] _folders;

    public DiskSequentialReadProbe() : this(AppContext.BaseDirectory, RuntimeEnvironment.GetRuntimeDirectory())
    {
    }

    internal DiskSequentialReadProbe(params string[] folders) => _folders = folders;

    public string Id => "disk.sequential";

    public string Name => "Sequential read";

    public BenchmarkCategory Category => BenchmarkCategory.Disk;

    public Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        FileInfo? file = BenchmarkFiles.Largest(_folders);
        if (file is null)
        {
            return BenchmarkMeasurement.Unavailable("MB/s", 500, true, BenchmarkFiles.NoFileReason);
        }

        byte[] buffer = GC.AllocateUninitializedArray<byte>(BufferBytes);
        long read = 0;
        using FileStream stream = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (read < TargetBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, TargetBytes - read));
            if (count == 0)
            {
                stream.Seek(0, SeekOrigin.Begin);
                continue;
            }

            read += count;
        }

        stopwatch.Stop();
        return new BenchmarkMeasurement(read / 1e6 / stopwatch.Elapsed.TotalSeconds, "MB/s", 500, HigherIsBetter: true);
    }, cancellationToken);
}

/// <summary>Reads 4 KiB blocks at random places in the largest file of the program: what a database or an index costs.</summary>
public sealed class DiskRandomReadProbe : IBenchmarkProbe
{
    private const int BlockBytes = 4096;
    private const int MaxReads = 4000;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(2);

    private readonly string[] _folders;

    public DiskRandomReadProbe() : this(AppContext.BaseDirectory, RuntimeEnvironment.GetRuntimeDirectory())
    {
    }

    internal DiskRandomReadProbe(params string[] folders) => _folders = folders;

    public string Id => "disk.random";

    public string Name => "Random read, 4 KiB";

    public BenchmarkCategory Category => BenchmarkCategory.Disk;

    public Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        FileInfo? file = BenchmarkFiles.Largest(_folders);
        if (file is null)
        {
            return BenchmarkMeasurement.Unavailable("IOPS", 20000, true, BenchmarkFiles.NoFileReason);
        }

        // A fixed seed: every run reads the same places, so two runs differ by the machine and not by the luck of the draw.
        var random = new Random(12345);
        long blocks = file.Length / BlockBytes;
        byte[] buffer = new byte[BlockBytes];
        using SafeFileHandle handle = File.OpenHandle(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);

        int reads = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (reads < MaxReads && stopwatch.Elapsed < MaxDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RandomAccess.Read(handle, buffer, random.NextInt64(blocks) * BlockBytes);
            reads++;
        }

        stopwatch.Stop();
        return new BenchmarkMeasurement(reads / stopwatch.Elapsed.TotalSeconds, "IOPS", 20000, HigherIsBetter: true);
    }, cancellationToken);
}

/// <summary>How many directory entries a recursive listing of the program folder yields a second. Only names and attributes are read.</summary>
public sealed class DiskListingProbe : IBenchmarkProbe
{
    private const int MaxEntries = 20000;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(2);

    private readonly string _folder;

    public DiskListingProbe() : this(AppContext.BaseDirectory)
    {
    }

    internal DiskListingProbe(string folder) => _folder = folder;

    public string Id => "disk.listing";

    public string Name => "Directory listing";

    public BenchmarkCategory Category => BenchmarkCategory.Disk;

    public Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        int entries = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        foreach (FileSystemInfo _ in new DirectoryInfo(_folder).EnumerateFileSystemInfos("*", options))
        {
            if (entries % 1024 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            entries++;
            if (entries >= MaxEntries || stopwatch.Elapsed >= MaxDuration)
            {
                break;
            }
        }

        stopwatch.Stop();
        if (entries == 0)
        {
            return BenchmarkMeasurement.Unavailable("k entries/s", 200, true, "the program folder has nothing to list");
        }

        return new BenchmarkMeasurement(entries / 1000.0 / stopwatch.Elapsed.TotalSeconds, "k entries/s", 200, HigherIsBetter: true);
    }, cancellationToken);
}
