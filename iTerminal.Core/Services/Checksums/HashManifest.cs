using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Services.Compare;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Services.Checksums;

/// <summary>What verifying one manifest row found.</summary>
public enum ManifestStatus
{
    /// <summary>The file is there and hashes to what the manifest says.</summary>
    Ok,

    /// <summary>The file is there and hashes to something else.</summary>
    Changed,

    /// <summary>The manifest lists it, the folder does not have it.</summary>
    Missing,

    /// <summary>The folder has it, the manifest does not list it.</summary>
    Extra,

    /// <summary>It could not be read, or the manifest names a path outside the folder.</summary>
    Unreadable,
}

/// <summary>One verified row: the path as the manifest writes it, what was found, and why when that needs saying.</summary>
public sealed record ManifestItem(string Path, ManifestStatus Status, string? Detail = null);

/// <summary>How far a create or a verify has got. Percent is by bytes when the drive reported sizes, by files otherwise.</summary>
public sealed record ManifestProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal)
{
    public int Percent => BytesTotal > 0
        ? (int)Math.Min(100, BytesDone * 100 / BytesTotal)
        : FilesTotal > 0 ? Math.Min(100, FilesDone * 100 / FilesTotal) : 0;
}

/// <summary>A file the manifest covers: where it is, and its path relative to the manifest's folder ('/'-separated).</summary>
public sealed record ManifestFile(string Path, Url Url, long Size);

/// <summary>A manifest that was built and is ready to be written.</summary>
public sealed record ManifestCreated(string FileName, string Text, int Files, IReadOnlyList<ManifestItem> Failures);

/// <summary>What writing the manifest did.</summary>
public enum ManifestWriteOutcome
{
    Saved,
    AlreadySaysSo,
    SaysSomethingElse,
    NotLocal,
    Failed,
}

public sealed record ManifestVerified(IReadOnlyList<ManifestItem> Items)
{
    public int Count(ManifestStatus status) => Items.Count(item => item.Status == status);

    public bool IsClean => Items.All(item => item.Status == ManifestStatus.Ok);

}

/// <summary>
/// A <c>SHA256SUMS</c>-style manifest for a set of files or a whole folder tree, on any drive: built
/// from what a panel marks, and checked back against the folder.
///
/// <para><b>Every file is read once</b>, with one algorithm, streamed through its drive's
/// <see cref="IFileSystem"/>; the listing walks through the drives' own listings
/// (<see cref="FolderComparer.ListAsync"/>), so a folder in an archive, a container or over ssh
/// is covered like a local one. Callers run this on a background task and pass the token: nothing
/// here is for a key handler.</para>
///
/// <para><b>Never over a manifest that says something else</b> — the same rule as
/// <c>name.sha256</c> in the checksum dialog: that file may be the publisher's.</para>
/// </summary>
public static class HashManifest
{
    /// <summary>A manifest larger than this is not a manifest.</summary>
    public const int MaxManifestBytes = 16 << 20;

    /// <summary>The file a manifest is conventionally written to: <c>SHA256SUMS</c>, <c>SHA512SUMS</c>, <c>SHA1SUMS</c>, <c>MD5SUMS</c>.</summary>
    public static string FileName(ChecksumAlgorithm algorithm) => ChecksumService.DisplayName(algorithm).Replace("-", string.Empty) + "SUMS";

    /// <summary>Whether <paramref name="name"/> is one of the conventional manifest names (case-insensitive, optionally <c>.txt</c>).</summary>
    public static bool IsManifestName(string name) =>
        ChecksumService.Algorithms.Any(a =>
            string.Equals(name, FileName(a), StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, FileName(a) + ".txt", StringComparison.OrdinalIgnoreCase));

    #region Listing
    /// <summary>
    /// The files <paramref name="roots"/> stand for, with paths relative to <paramref name="baseFolder"/>:
    /// a file is itself, a folder is everything under it. No roots means the whole of the base folder.
    /// </summary>
    /// <param name="skip">A path (relative, as written) to leave out — the manifest itself.</param>
    public static async Task<List<ManifestFile>> ListAsync(
        Url baseFolder,
        IReadOnlyList<Url> roots,
        IFileSystem fileSystem,
        Func<string, bool>? skip = null,
        CancellationToken cancellationToken = default)
    {
        List<ManifestFile> files = [];
        Stack<(Url Folder, string Prefix)> pending = [];
        if (roots.Count == 0)
        {
            pending.Push((baseFolder, string.Empty));
        }

        foreach (Url root in roots)
        {
            string name = FocusedEntry.NameOf(root);
            if (root.PathType == PathType.Folder)
            {
                pending.Push((root, name + "/"));
            }
            else
            {
                files.Add(new ManifestFile(name, root, Math.Max(0, root.Length)));
            }
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (Url folder, string prefix) = pending.Pop();
            foreach (FolderComparer.Node node in await FolderComparer.ListAsync(folder, fileSystem, cancellationToken))
            {
                string path = prefix + node.Name;
                if (node.IsFolder)
                {
                    if (!ContentModelFolderTaskHelper.IsLinkBackToAncestor(node.Url))
                    {
                        pending.Push((node.Url, path + "/"));
                    }
                }
                else
                {
                    files.Add(new ManifestFile(path, node.Url, node.Size ?? Math.Max(0, node.Url.Length)));
                }
            }
        }

        files.RemoveAll(file => skip?.Invoke(file.Path) == true);
        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return files;
    }
    #endregion

    #region Create
    /// <summary>
    /// Hashes every file and builds the manifest text (<c>hash *path</c>, one per line, sorted). A file
    /// that cannot be read is a failure, and a manifest with failures is not written: a list that
    /// silently lacks files would pass a verify that should not.
    /// </summary>
    public static async Task<ManifestCreated> CreateAsync(
        Url baseFolder,
        IReadOnlyList<Url> roots,
        IFileSystem fileSystem,
        ChecksumAlgorithm algorithm,
        IProgress<ManifestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string manifestName = FileName(algorithm);
        List<ManifestFile> files = await ListAsync(baseFolder, roots, fileSystem, path => string.Equals(path, manifestName, StringComparison.OrdinalIgnoreCase), cancellationToken);
        long bytesTotal = files.Sum(file => file.Size);
        long bytesBefore = 0;
        int done = 0;
        List<string> lines = [];
        List<ManifestItem> failures = [];
        progress?.Report(new ManifestProgress(0, files.Count, 0, bytesTotal));
        foreach (ManifestFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long before = bytesBefore;
            Reporter reporter = new(read => progress?.Report(new ManifestProgress(done, files.Count, before + read, bytesTotal)));
            try
            {
                string hash = await ChecksumService.ComputeOneAsync(file.Url, fileSystem, algorithm, reporter, cancellationToken);
                lines.Add(ChecksumService.FormatChecksumLine(hash, file.Path));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new ManifestItem(file.Path, ManifestStatus.Unreadable, exception.Message));
            }

            bytesBefore += file.Size;
            done++;
            progress?.Report(new ManifestProgress(done, files.Count, bytesBefore, bytesTotal));
        }

        return new ManifestCreated(manifestName, lines.Count == 0 ? string.Empty : string.Join("\n", lines) + "\n", lines.Count, failures);
    }

    /// <summary>
    /// Writes the manifest into <paramref name="baseFolder"/> on the local disk — as the checksum
    /// dialog writes <c>name.sha256</c>, and as it never over a file that says something else.
    /// </summary>
    public static (ManifestWriteOutcome Outcome, string Message, string Detail) Write(Url baseFolder, ManifestCreated manifest)
    {
        if (baseFolder.ContainerType != ContainerType.FileSystem)
        {
            return (ManifestWriteOutcome.NotLocal, "A manifest can only be written into a folder on the local disk.", string.Empty);
        }

        string target = Path.Combine(baseFolder.FullName, manifest.FileName);
        try
        {
            if (File.Exists(target))
            {
                return Says(File.ReadAllText(target), manifest.Text)
                    ? (ManifestWriteOutcome.AlreadySaysSo, $"{manifest.FileName} already says so.", string.Empty)
                    : (ManifestWriteOutcome.SaysSomethingElse, $"{manifest.FileName} exists and says something else — not overwritten.", string.Empty);
            }

            // CreateNew: a manifest that appears between the check and the write is not replaced either.
            using (FileStream stream = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(manifest.Text);
            }

            return (ManifestWriteOutcome.Saved, $"Saved {manifest.FileName}: {manifest.Files} file{(manifest.Files == 1 ? string.Empty : "s")}.", string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (ManifestWriteOutcome.Failed, $"Could not save {manifest.FileName}: {exception.Message}", exception.Message);
        }
    }

    /// <summary>Whether the existing manifest holds every row of the new one, with the same hash.</summary>
    private static bool Says(string existing, string created)
    {
        Dictionary<string, string> had = [];
        foreach (ChecksumService.ManifestEntry entry in ChecksumService.ParseManifest(existing))
        {
            had[entry.Path] = entry.Hash;
        }

        IReadOnlyList<ChecksumService.ManifestEntry> wanted = ChecksumService.ParseManifest(created);
        return wanted.All(entry => had.TryGetValue(entry.Path, out string? hash) && string.Equals(hash, entry.Hash, StringComparison.OrdinalIgnoreCase))
               && had.Count == wanted.Count;
    }
    #endregion

    #region Verify
    /// <summary>
    /// Checks <paramref name="manifestUrl"/> against <paramref name="baseFolder"/>: every row of the
    /// manifest is OK, changed, missing or unreadable, and every file under the folder that the
    /// manifest does not list is extra. Each file is read once, with the algorithm its row's hash length says.
    /// </summary>
    /// <param name="onItem">Called as each row is decided, from the calling task.</param>
    public static async Task<ManifestVerified> VerifyAsync(
        Url manifestUrl,
        Url baseFolder,
        IFileSystem fileSystem,
        IProgress<ManifestProgress>? progress = null,
        Action<ManifestItem>? onItem = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChecksumService.ManifestEntry> entries = ChecksumService.ParseManifest(await ReadTextAsync(manifestUrl, fileSystem, cancellationToken));
        if (entries.Count == 0)
        {
            throw new InvalidDataException("The manifest holds no checksums.");
        }

        string manifestName = FocusedEntry.NameOf(manifestUrl);
        List<ManifestFile> tree = await ListAsync(baseFolder, [], fileSystem, path => string.Equals(path, manifestName, StringComparison.OrdinalIgnoreCase), cancellationToken);
        StringComparer comparer = baseFolder.ContainerType == ContainerType.FileSystem && !OperatingSystem.IsWindows() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        Dictionary<string, ManifestFile> present = new(comparer);
        foreach (ManifestFile file in tree)
        {
            present[file.Path] = file;
        }

        long bytesTotal = entries.Sum(entry => present.TryGetValue(entry.Path, out ManifestFile? file) ? file.Size : 0);
        long bytesBefore = 0;
        int total = entries.Count;
        int done = 0;
        List<ManifestItem> items = [];
        void Add(ManifestItem item)
        {
            items.Add(item);
            onItem?.Invoke(item);
        }

        progress?.Report(new ManifestProgress(0, total, 0, bytesTotal));
        HashSet<string> listed = new(comparer);
        foreach (ChecksumService.ManifestEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            listed.Add(entry.Path);
            if (IsOutsideTheFolder(entry.Path))
            {
                Add(new ManifestItem(entry.Path, ManifestStatus.Unreadable, "the path leaves the manifest's folder"));
            }
            else if (!present.TryGetValue(entry.Path, out ManifestFile? file))
            {
                Add(new ManifestItem(entry.Path, ManifestStatus.Missing));
            }
            else
            {
                long before = bytesBefore;
                Reporter reporter = new(read => progress?.Report(new ManifestProgress(done, total, before + read, bytesTotal)));
                try
                {
                    string hash = await ChecksumService.ComputeOneAsync(file.Url, fileSystem, entry.Algorithm, reporter, cancellationToken);
                    Add(new ManifestItem(entry.Path, string.Equals(hash, entry.Hash, StringComparison.OrdinalIgnoreCase) ? ManifestStatus.Ok : ManifestStatus.Changed));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Add(new ManifestItem(entry.Path, ManifestStatus.Unreadable, exception.Message));
                }

                bytesBefore += file.Size;
            }

            done++;
            progress?.Report(new ManifestProgress(done, total, bytesBefore, bytesTotal));
        }

        foreach (ManifestFile file in tree.Where(file => !listed.Contains(file.Path)))
        {
            Add(new ManifestItem(file.Path, ManifestStatus.Extra));
        }

        return new ManifestVerified(items);
    }

    /// <summary>A manifest is read for what it says about files in its own folder: an absolute path or a <c>..</c> is never followed.</summary>
    private static bool IsOutsideTheFolder(string path) =>
        path.StartsWith('/') || path.Contains(':') || path.Split('/').Contains("..");

    private static async Task<string> ReadTextAsync(Url url, IFileSystem fileSystem, CancellationToken cancellationToken)
    {
        if (url.ContainerType == ContainerType.FileSystem)
        {
            if (new FileInfo(url.FullName).Length > MaxManifestBytes)
            {
                throw new InvalidDataException("That file is too large to be a manifest.");
            }

            return await File.ReadAllTextAsync(url.FullName, cancellationToken);
        }

        using MemoryStream buffer = new();
        await fileSystem.StreamToAsync(url, new LimitedStream(buffer, MaxManifestBytes), null, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
    #endregion

    /// <summary><see cref="Progress{T}"/> posts to a synchronization context; this reports where it is called.</summary>
    private sealed class Reporter(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    /// <summary>Passes writes on to a stream until a limit, then fails: a drive that streams without end must not fill the memory.</summary>
    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (inner.Length + buffer.Length > limit)
            {
                throw new InvalidDataException("That file is too large to be a manifest.");
            }

            inner.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
