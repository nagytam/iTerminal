using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using iTerminal.Core.Helper;
using System.Text;

namespace iTerminal.Core.Services.Archives;

/// <summary>
/// ZIP, TAR, TAR.GZ and TAR.BZ2, written in-process with SharpZipLib.
///
/// <para>SharpZipLib rather than <c>System.IO.Compression</c> for one reason: the BCL cannot write
/// an encrypted ZIP. SharpZipLib writes both AES-256 and the old ZipCrypto, and its TAR and BZip2
/// streams cover the rest, so the four formats share one code path.</para>
///
/// <para><b>Adding to an existing archive rewrites it.</b> None of these formats can be appended to
/// in place once compressed — a TAR.GZ is one compressed stream, and an encrypted ZIP entry cannot
/// be written by SharpZipLib's in-place updater — so the archive is read entry by entry into a new
/// one beside it, the new files are added, and the new archive is moved over the old one only once
/// it is complete. The old entries are recompressed on the way; an encrypted entry can only be
/// carried over with the password that encrypted it, which is the password the dialog was given.</para>
/// </summary>
public static class ManagedArchiveWriter
{
    public static async Task<CompressionResult> WriteAsync(
        string archivePath,
        IReadOnlyList<ArchiveItem> items,
        CompressionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(archivePath))!;
        var temp = Path.Combine(folder, $".{Path.GetFileName(archivePath)}.{Guid.NewGuid():N}.tmp");
        var exists = File.Exists(archivePath);
        var newNames = new HashSet<string>(items.Select(i => NormalizeName(i.EntryName)), StringComparer.Ordinal);
        var written = new HashSet<string>(StringComparer.Ordinal);
        int added = 0, kept = 0, skipped = 0;
        var tracker = new ProgressTracker(items, progress);

        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: false))
            {
                using var sink = CreateSink(output, options);
                if (exists)
                {
                    foreach (var entry in ReadExisting(archivePath, options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var name = NormalizeName(entry.Name);
                        using (entry)
                        {
                            if ((options.ReplaceExisting && newNames.Contains(name)) || !written.Add(name))
                            {
                                continue;
                            }

                            if (entry.IsFolder)
                            {
                                sink.AddFolder(name, entry.Modified);
                            }
                            else
                            {
                                await sink.AddFileAsync(name, entry.Modified, entry.Size, (s, ct) => entry.Content!.CopyToAsync(s, ct), cancellationToken);
                            }
                        }

                        kept++;
                    }
                }

                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = NormalizeName(item.EntryName);
                    if (!written.Add(name))
                    {
                        // Already in the archive and replacing is off — or a folder entry it had.
                        if (!item.IsFolder)
                        {
                            skipped++;
                        }

                        tracker.Skip(item);
                        continue;
                    }

                    tracker.Start(item);
                    if (item.IsFolder)
                    {
                        sink.AddFolder(name, item.Modified);
                    }
                    else
                    {
                        var knownSize = item.LocalPath is not null ? new FileInfo(item.LocalPath).Length : (long?)null;
                        await sink.AddFileAsync(
                            name,
                            item.Modified,
                            knownSize,
                            (s, ct) => item.WriteToAsync(new ProgressStream(s, tracker.BytesOf(item)), ct),
                            cancellationToken);
                        added++;
                    }

                    tracker.Finish(item);
                }

                sink.Finish();
            }

            File.Move(temp, archivePath, overwrite: true);
            return new CompressionResult(added, kept, skipped);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>'/'-separated, no leading slash — what every one of these formats stores.</summary>
    internal static string NormalizeName(string? name)
    {
        var normalized = (name ?? string.Empty).Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A temp file that cannot be removed is litter, not a reason to hide the real error.
        }
    }

    #region Writing
    private interface IEntrySink : IDisposable
    {
        void AddFolder(string name, DateTime modified);

        /// <param name="size">The length when known; a format that must know it up front stages the bytes otherwise.</param>
        Task AddFileAsync(string name, DateTime modified, long? size, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken);

        void Finish();
    }

    private static IEntrySink CreateSink(Stream output, CompressionOptions options)
    {
        var format = options.Format;
        if (format == ArchiveFormat.Zip)
        {
            return new ZipSink(output, options);
        }

        if (format == ArchiveFormat.Tar)
        {
            return new TarSink(output);
        }

        if (format == ArchiveFormat.TarGz)
        {
            var gzip = new GZipOutputStream(output) { IsStreamOwner = false };
            gzip.SetLevel(Math.Clamp(options.Level, 1, 9));
            return new TarSink(gzip, compressor: gzip);
        }

        if (format == ArchiveFormat.TarBz2)
        {
            var bzip2 = new BZip2OutputStream(output, Math.Clamp(options.Level, 1, 9)) { IsStreamOwner = false };
            return new TarSink(bzip2, compressor: bzip2);
        }

        throw new NotSupportedException($"{format.Name} is not written in-process.");
    }

    private sealed class ZipSink : IEntrySink
    {
        private readonly ZipOutputStream _zip;
        private readonly int _aesKeySize;

        public ZipSink(Stream output, CompressionOptions options)
        {
            _zip = new ZipOutputStream(output) { IsStreamOwner = false, UseZip64 = UseZip64.Dynamic };
            _zip.SetLevel(Math.Clamp(options.Level, 0, 9));
            if (options.HasPassword)
            {
                _zip.Password = options.Password;
                _aesKeySize = options.Encryption == ArchiveEncryption.Aes256 ? 256 : 0;
            }
        }

        public void AddFolder(string name, DateTime modified)
        {
            var entry = new ZipEntry(name.EndsWith('/') ? name : name + "/") { DateTime = modified, IsUnicodeText = true };
            _zip.PutNextEntry(entry);
            _zip.CloseEntry();
        }

        public async Task AddFileAsync(string name, DateTime modified, long? size, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
        {
            var entry = new ZipEntry(name)
            {
                DateTime = modified,
                IsUnicodeText = true,
                AESKeySize = _aesKeySize,
            };
            if (size is { } known)
            {
                entry.Size = known;
            }

            _zip.PutNextEntry(entry);
            await write(new ProgressStream(_zip, null), cancellationToken);
            _zip.CloseEntry();
        }

        public void Finish() => _zip.Finish();

        public void Dispose() => _zip.Dispose();
    }

    /// <summary>
    /// A TAR header carries the file's length before its bytes, so a file whose length is not known
    /// — anything not on the local disk — is staged to a temporary file first. Remote listings
    /// report sizes, but a size that is off by a byte would corrupt every entry after it.
    /// </summary>
    private sealed class TarSink(Stream output, Stream? compressor = null) : IEntrySink
    {
        private readonly TarOutputStream _tar = new(output, Encoding.UTF8) { IsStreamOwner = false };

        public void AddFolder(string name, DateTime modified)
        {
            var entry = TarEntry.CreateTarEntry(name.EndsWith('/') ? name : name + "/");
            entry.TarHeader.TypeFlag = TarHeader.LF_DIR;
            entry.ModTime = modified.ToUniversalTime();
            entry.Size = 0;
            _tar.PutNextEntry(entry);
            _tar.CloseEntry();
        }

        public async Task AddFileAsync(string name, DateTime modified, long? size, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
        {
            if (size is null)
            {
                var staged = Path.GetTempFileName();
                try
                {
                    await using (var stage = new FileStream(staged, FileMode.Create, FileAccess.Write))
                    {
                        await write(stage, cancellationToken);
                    }

                    await using var read = new FileStream(staged, FileMode.Open, FileAccess.Read);
                    await AddKnownAsync(name, modified, read.Length, (s, ct) => read.CopyToAsync(s, ct), cancellationToken);
                }
                finally
                {
                    TryDelete(staged);
                }

                return;
            }

            await AddKnownAsync(name, modified, size.Value, write, cancellationToken);
        }

        private async Task AddKnownAsync(string name, DateTime modified, long size, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
        {
            var entry = TarEntry.CreateTarEntry(name);
            entry.ModTime = modified.ToUniversalTime();
            entry.Size = size;
            _tar.PutNextEntry(entry);
            await write(new ProgressStream(_tar, null), cancellationToken);
            _tar.CloseEntry();
        }

        public void Finish()
        {
            _tar.Finish();
            _tar.Dispose();
            if (compressor is GZipOutputStream gzip)
            {
                gzip.Finish();
            }

            compressor?.Dispose();
        }

        public void Dispose()
        {
            _tar.Dispose();
            compressor?.Dispose();
        }
    }
    #endregion

    #region Reading the archive being added to
    private sealed class ExistingEntry(string name, bool isFolder, DateTime modified, long size, Stream? content, IDisposable? owner = null) : IDisposable
    {
        public string Name => name;
        public bool IsFolder => isFolder;
        public DateTime Modified => modified;
        public long Size => size;
        public Stream? Content => content;

        public void Dispose()
        {
            content?.Dispose();
            owner?.Dispose();
        }
    }

    private static IEnumerable<ExistingEntry> ReadExisting(string archivePath, CompressionOptions options)
    {
        var format = options.Format;
        if (format == ArchiveFormat.Zip)
        {
            using var zip = new ZipFile(archivePath);
            if (options.HasPassword)
            {
                zip.Password = options.Password;
            }

            foreach (ZipEntry entry in zip)
            {
                if (entry.IsDirectory)
                {
                    yield return new ExistingEntry(entry.Name, true, entry.DateTime, 0, null);
                    continue;
                }

                if (entry.IsCrypted && !options.HasPassword)
                {
                    throw new InvalidOperationException(
                        $"The archive is encrypted ('{entry.Name}'): type its password to add to it.");
                }

                Stream content;
                try
                {
                    content = zip.GetInputStream(entry);
                }
                catch (ZipException exception) when (entry.IsCrypted)
                {
                    throw new InvalidOperationException(
                        $"The password does not open '{entry.Name}' in the archive: {exception.Message}", exception);
                }

                yield return new ExistingEntry(entry.Name, false, entry.DateTime, entry.Size, content);
            }

            yield break;
        }

        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using Stream decompressed = format == ArchiveFormat.TarGz ? new GZipInputStream(file) { IsStreamOwner = false }
            : format == ArchiveFormat.TarBz2 ? new BZip2InputStream(file) { IsStreamOwner = false }
            : new NonClosingStream(file);
        using var tar = new TarInputStream(decompressed, Encoding.UTF8) { IsStreamOwner = false };
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.IsDirectory)
            {
                yield return new ExistingEntry(entry.Name, true, entry.ModTime.ToLocalTime(), 0, null);
                continue;
            }

            if (entry.TarHeader.TypeFlag is not (TarHeader.LF_NORMAL or TarHeader.LF_OLDNORM))
            {
                // Links, devices and the like: carried by name only would be a lie; leave them.
                continue;
            }

            // The entry is buffered: the reader cannot be read from while the writer is being fed
            // asynchronously by another entry's enumeration.
            var buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
            tar.CopyEntryContents(buffer);
            buffer.Position = 0;
            yield return new ExistingEntry(entry.Name, false, entry.ModTime.ToLocalTime(), entry.Size, buffer);
        }
    }

    /// <summary>Lets a reader be disposed without closing the file under it.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    #endregion

    /// <summary>Turns per-item byte counts into one running figure for the progress window.</summary>
    private sealed class ProgressTracker(IReadOnlyList<ArchiveItem> items, IProgress<ArchiveProgress>? progress)
    {
        private readonly long _total = items.Where(i => !i.IsFolder).Sum(i => Math.Max(0, i.Size));
        private readonly int _count = items.Count;
        private long _finishedBytes;
        private int _done;
        private string _current = string.Empty;

        public void Start(ArchiveItem item)
        {
            _current = item.EntryName;
            Report(0);
        }

        public IProgress<long> BytesOf(ArchiveItem item) => new SynchronousProgress<long>(Report);

        public void Finish(ArchiveItem item)
        {
            _finishedBytes += Math.Max(0, item.Size);
            _done++;
            Report(0);
        }

        public void Skip(ArchiveItem item) => Finish(item);

        private void Report(long currentBytes) =>
            progress?.Report(new ArchiveProgress(_finishedBytes + currentBytes, _total, _done, _count, _current));
    }
}
