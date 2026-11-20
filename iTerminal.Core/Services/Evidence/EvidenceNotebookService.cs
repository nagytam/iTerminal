using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Services.Evidence;

/// <summary>
/// The notebook on disk. Layout of the notebook folder:
/// <list type="bullet">
/// <item><c>notebook.json</c>: the index, every entry's record and the notebook's own origin.</item>
/// <item><c>items/NNNN_&lt;name&gt;</c>: the content of each entry, numbered in the order it was added.</item>
/// </list>
///
/// <para><b>Each change rewrites the index whole, through a temporary file.</b> The index is small, so
/// this costs nothing, and a crash leaves either the old index or the new one.</para>
///
/// <para><b>A file is read once.</b> <see cref="AddFileAsync"/> hashes the bytes as they are written
/// into the notebook, so the digest recorded is the digest of what was stored, not of a second read.</para>
///
/// <para><b>Sequence numbers are reserved before the copy starts</b>, so two adds in flight never share a
/// name. A failed copy leaves a gap in the numbering, which is correct: an entry was attempted there.</para>
/// </summary>
public sealed class EvidenceNotebookService : IEvidenceNotebookService
{
    /// <summary>The index file's name in the notebook folder.</summary>
    public const string IndexFileName = "notebook.json";

    /// <summary>The folder inside the notebook that holds the entries' content.</summary>
    public const string ItemsFolderName = "items";

    /// <summary>The sample rate of microphone recordings, from the speech capture: 16 kHz, mono, 16-bit.</summary>
    public const int VoiceSampleRate = 16000;

    private const int MaxFileNameLength = 120;
    private const int MaxExtensionLength = 16;
    private const int MaxNoteTitleLength = 60;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Lock _sync = new();
    private EvidenceNotebookIndex? _index;
    private string? _folder;
    private int _nextSequence = 1;

    /// <inheritdoc />
    public string? FolderPath
    {
        get
        {
            lock (_sync)
            {
                return _folder;
            }
        }
    }

    /// <inheritdoc />
    public async Task OpenAsync(string folder, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(folder);
        Directory.CreateDirectory(Path.Combine(full, ItemsFolderName));

        var indexPath = Path.Combine(full, IndexFileName);
        EvidenceNotebookIndex index;
        if (File.Exists(indexPath))
        {
            var text = await File.ReadAllTextAsync(indexPath, cancellationToken);
            index = JsonSerializer.Deserialize<EvidenceNotebookIndex>(text)
                ?? throw new InvalidDataException($"{indexPath} is empty.");
            if (index.Format != EvidenceNotebookIndex.CurrentFormat)
            {
                throw new InvalidDataException($"{indexPath} is notebook format {index.Format}; this version reads format {EvidenceNotebookIndex.CurrentFormat}.");
            }

            index = index with { Entries = index.Entries ?? new List<EvidenceRecord>() };
            var outside = index.Entries.FirstOrDefault(entry => !IsItemName(entry.FileName));
            if (outside is not null)
            {
                throw new InvalidDataException($"{indexPath} names an entry outside the items folder: {outside.FileName}");
            }
        }
        else
        {
            index = new EvidenceNotebookIndex(
                EvidenceNotebookIndex.CurrentFormat,
                Guid.NewGuid().ToString("N"),
                EvidenceOriginCapture.Capture(full, "NewNotebook", null, null, DateTimeOffset.Now),
                []);
        }

        lock (_sync)
        {
            _index = index;
            _folder = full;
            _nextSequence = index.Entries.Count == 0 ? 1 : index.Entries.Max(entry => entry.Sequence) + 1;
            WriteIndex(full, index);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EvidenceRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            IReadOnlyList<EvidenceRecord> entries = _index is null ? Array.Empty<EvidenceRecord>() : _index.Entries.ToList();
            return Task.FromResult(entries);
        }
    }

    /// <inheritdoc />
    public async Task<EvidenceRecord> AddFileAsync(IFileSystem source, Url sourceUrl, long? sourceSize, DateTime? sourceModifiedUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceUrl);
        cancellationToken.ThrowIfCancellationRequested();

        var (folder, sequence) = Reserve();
        var title = string.IsNullOrEmpty(sourceUrl.Name) ? "file" : sourceUrl.Name;
        var fileName = ContentName(sequence, title);
        var path = Path.Combine(folder, ItemsFolderName, fileName);
        var origin = EvidenceOriginCapture.Capture(sourceUrl.FullUrl, sourceUrl.ContainerType.ToString(), sourceSize, sourceModifiedUtc, DateTimeOffset.Now);

        string digest;
        long size;
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            await using var hashing = new HashingStream(file);
            await source.StreamToAsync(sourceUrl, hashing, null, cancellationToken);
            await hashing.FlushAsync(cancellationToken);
            digest = hashing.TakeDigest();
            size = hashing.BytesWritten;
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        var record = new EvidenceRecord(sequence, fileName, EvidenceKind.File, title, size, digest, origin);
        Commit(folder, record);
        return record;
    }

    /// <inheritdoc />
    public Task<EvidenceRecord> AddTextAsync(EvidenceKind kind, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var now = DateTimeOffset.Now;
        return kind switch
        {
            EvidenceKind.Note => WriteContentAsync(kind, NoteTitle(text), "note.txt", Encoding.UTF8.GetBytes(text),
                EvidenceOriginCapture.Capture("typed in the notebook", kind.ToString(), null, null, now), null, cancellationToken),
            EvidenceKind.Clipboard => WriteContentAsync(kind, $"Clipboard text ({text.Length} characters)", "clipboard.txt", Encoding.UTF8.GetBytes(text),
                EvidenceOriginCapture.Capture("clipboard", kind.ToString(), null, null, now), null, cancellationToken),
            EvidenceKind.Link => WriteContentAsync(kind, text.Trim(), "link.txt", Encoding.UTF8.GetBytes(text.Trim()),
                EvidenceOriginCapture.Capture(text.Trim(), kind.ToString(), null, null, now), null, cancellationToken),
            _ => throw new ArgumentException($"{kind} is not a text kind; use AddFileAsync or AddVoiceAsync.", nameof(kind)),
        };
    }

    /// <inheritdoc />
    public Task<EvidenceRecord> AddVoiceAsync(ReadOnlyMemory<byte> pcm, int sampleRate, CancellationToken cancellationToken = default)
    {
        if (pcm.IsEmpty)
        {
            throw new ArgumentException("A recording needs audio.", nameof(pcm));
        }

        // The WAV bytes are built here, synchronously, because a span cannot cross an await.
        var wav = WavFile.Build(pcm.Span, sampleRate);
        var seconds = pcm.Length / (double)(sampleRate * 2);
        var origin = EvidenceOriginCapture.Capture("microphone", EvidenceKind.Voice.ToString(), null, null, DateTimeOffset.Now);
        var title = $"Voice recording, {TimeSpan.FromSeconds(seconds):m\\:ss}";
        return WriteContentAsync(EvidenceKind.Voice, title, "voice.wav", wav, origin, seconds, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EvidenceProblem>> VerifyAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EvidenceRecord> entries;
        string folder;
        lock (_sync)
        {
            folder = _folder ?? throw new InvalidOperationException("No notebook is open.");
            entries = _index is null ? Array.Empty<EvidenceRecord>() : _index.Entries.ToList();
        }

        var problems = new List<EvidenceProblem>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(folder, ItemsFolderName, entry.FileName);
            if (!File.Exists(path))
            {
                problems.Add(new EvidenceProblem(entry.FileName, UiText.Get("Evidence.Service.Missing", "The content file is missing.")));
                continue;
            }

            if (new FileInfo(path).Length != entry.Size)
            {
                problems.Add(new EvidenceProblem(entry.FileName, UiText.Get("Evidence.Service.SizeChanged", "The content has a different size from the one recorded.")));
                continue;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(digest, entry.Sha256, StringComparison.Ordinal))
            {
                problems.Add(new EvidenceProblem(entry.FileName, UiText.Get("Evidence.Service.HashChanged", "The content no longer matches its recorded SHA-256.")));
            }
        }

        return problems;
    }

    #region IFileSystem (read-only)

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        var (_, path) = Locate(url);
        return new MemoryFile(url, await File.ReadAllBytesAsync(path));
    }

    /// <inheritdoc />
    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (_, path) = Locate(url);
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            progress?.Report(total);
        }
    }

    /// <inheritdoc />
    public bool ExistsFile(Url url) => Find(url) is not null;

    /// <inheritdoc />
    public bool ExistsFolder(Url url) => url.Path.Trim('/').Length == 0;

    /// <inheritdoc />
    public string GetAvailableDiskSpace() => string.Empty;

    /// <inheritdoc />
    public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw ReadOnly();

    /// <inheritdoc />
    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw ReadOnly();

    /// <inheritdoc />
    public Task CreateFolderAsync(Url url) => throw ReadOnly();

    /// <inheritdoc />
    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnly();

    /// <inheritdoc />
    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnly();

    /// <inheritdoc />
    public bool DeleteFile(Url url, bool moveToRecycleBin) => throw ReadOnly();

    /// <inheritdoc />
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => throw ReadOnly();

    private static NotSupportedException ReadOnly() =>
        new(UiText.Get("Evidence.Service.ReadOnly", "Evidence is not changed once it is in the notebook. Add new evidence instead."));

    #endregion

    private (string Folder, int Sequence) Reserve()
    {
        lock (_sync)
        {
            if (_folder is null)
            {
                throw new InvalidOperationException("No notebook is open.");
            }

            return (_folder, _nextSequence++);
        }
    }

    private async Task<EvidenceRecord> WriteContentAsync(EvidenceKind kind, string title, string baseName, byte[] bytes, EvidenceOrigin origin, double? duration, CancellationToken cancellationToken)
    {
        var (folder, sequence) = Reserve();
        var fileName = ContentName(sequence, baseName);
        var path = Path.Combine(folder, ItemsFolderName, fileName);
        try
        {
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        var record = new EvidenceRecord(sequence, fileName, kind, title, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), origin, duration);
        Commit(folder, record);
        return record;
    }

    private void Commit(string folder, EvidenceRecord record)
    {
        lock (_sync)
        {
            if (_index is null)
            {
                throw new InvalidOperationException("No notebook is open.");
            }

            _index.Entries.Add(record);
            WriteIndex(folder, _index);
        }
    }

    private static void WriteIndex(string folder, EvidenceNotebookIndex index)
    {
        var path = Path.Combine(folder, IndexFileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(index, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private EvidenceRecord? Find(Url url)
    {
        var name = url.Path.Trim('/');
        lock (_sync)
        {
            return _index?.Entries.FirstOrDefault(entry => string.Equals(entry.FileName, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private (EvidenceRecord Record, string Path) Locate(Url url)
    {
        var record = Find(url) ?? throw new FileNotFoundException($"{url.Path} is not in the evidence notebook.");
        string folder;
        lock (_sync)
        {
            folder = _folder ?? throw new InvalidOperationException("No notebook is open.");
        }

        return (record, Path.Combine(folder, ItemsFolderName, record.FileName));
    }

    private static string ContentName(int sequence, string title) => $"{sequence:D4}_{SafeFileName(title)}";

    private static string NoteTitle(string text)
    {
        var firstLine = text.Split('\n', 2)[0].Trim();
        if (firstLine.Length == 0)
        {
            return "Note";
        }

        return firstLine.Length <= MaxNoteTitleLength ? firstLine : firstLine[..MaxNoteTitleLength] + "…";
    }

    /// <summary>
    /// A name that is safe as one file name on every system: separators and control characters become
    /// underscores, leading and trailing dots and blanks go, and the extension survives a long name.
    /// </summary>
    internal static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => c is '/' or '\\' || char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray())
            .Trim()
            .Trim('.');
        if (cleaned.Length == 0)
        {
            return "item";
        }

        if (cleaned.Length <= MaxFileNameLength)
        {
            return cleaned;
        }

        var extension = Path.GetExtension(cleaned);
        if (extension.Length > MaxExtensionLength)
        {
            extension = string.Empty;
        }

        return cleaned[..(MaxFileNameLength - extension.Length)] + extension;
    }

    /// <summary>A content file's name in the items folder: one plain name, no separators, not a relative step.</summary>
    internal static bool IsItemName(string name) =>
        name.Length > 0 && name is not ("." or "..") && Path.GetFileName(name) == name;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The copy has already failed, and that is the error the caller needs to see.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A write-only stream that passes bytes on to a file and hashes them on the way, so the digest of a
    /// copy is taken from the bytes that were stored.
    /// </summary>
    private sealed class HashingStream(Stream inner) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private string? _digest;

        public long BytesWritten { get; private set; }

        public string TakeDigest() => _digest ??= Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            _hash.AppendData(buffer);
            BytesWritten += buffer.Length;
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            _hash.AppendData(buffer.Span);
            BytesWritten += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
