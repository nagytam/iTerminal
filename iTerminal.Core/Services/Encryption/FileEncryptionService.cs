using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using iConsole.Data;

namespace iTerminal.Core.Services.Encryption;

/// <summary>Whether a run writes <c>.enc</c> files or reads them back.</summary>
public enum EncryptionMode
{
    Encrypt,
    Decrypt,
}

/// <summary>Why a file could not be decrypted; drives the message the dialog shows.</summary>
public enum EncryptionFailure
{
    /// <summary>The file does not start with the format's magic bytes.</summary>
    NotEncrypted,

    /// <summary>A newer version, or a KDF or cipher this build does not know.</summary>
    Unsupported,

    /// <summary>The header is cut short or carries values outside the accepted bounds.</summary>
    BadHeader,

    /// <summary>The first chunk failed authentication: the passphrase is wrong, or the start of the file is damaged.</summary>
    WrongPassphraseOrCorrupt,

    /// <summary>A later chunk failed authentication, or the file ends early: the file is damaged or was changed.</summary>
    Corrupt,
}

/// <summary>A file that cannot be decrypted, with the reason as a value so the caller can word it.</summary>
public sealed class EncryptionException(EncryptionFailure failure, string message) : IOException(message)
{
    public EncryptionFailure Failure { get; } = failure;
}

/// <summary>One file of a run: what was read, what was written, or why neither happened.</summary>
public sealed record EncryptionItem(string Source, string? Target, string? Error)
{
    public bool Succeeded => Error is null && Target is not null;
}

/// <summary>The outcome of a run over several files.</summary>
public sealed record EncryptionResult(IReadOnlyList<EncryptionItem> Items, bool Cancelled)
{
    public int Succeeded => Items.Count(item => item.Succeeded);

    public int Failed => Items.Count(item => !item.Succeeded);
}

/// <summary>Cumulative progress of a run, in bytes of input.</summary>
public sealed record EncryptionProgress(long BytesDone, long BytesTotal, string CurrentFile);

/// <summary>
/// Encrypts and decrypts files with a passphrase. The format is described in
/// <c>docs/architecture/file-operations.md</c> ("File encryption"): AES-256-GCM in 1 MiB chunks, a key from the passphrase
/// by PBKDF2-HMAC-SHA256, a header that is authenticated with every chunk, and a final-chunk flag that makes a cut file fail.
/// </summary>
public interface IFileEncryptionService
{
    /// <summary>Where the output of <paramref name="source"/> goes: <c>x.enc</c> for <c>x</c>; <c>x</c> for <c>x.enc</c>, else <c>x.decrypted</c>.</summary>
    string TargetPathFor(string source, EncryptionMode mode);

    Task EncryptAsync(Stream input, Stream output, string passphrase, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    Task DecryptAsync(Stream input, Stream output, string passphrase, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs every file of <paramref name="paths"/> to a separate output, one after another. A file that fails is listed and the
    /// others still run. An output is written to a temporary name beside the target and moved into place; an existing file is never replaced.
    /// </summary>
    Task<EncryptionResult> ProcessAsync(
        IReadOnlyList<string> paths,
        EncryptionMode mode,
        string passphrase,
        IProgress<EncryptionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IFileEncryptionService"/>
public sealed class FileEncryptionService(IFileSystemService fileSystem) : IFileEncryptionService
{
    /// <summary>The extension of an encrypted file.</summary>
    public const string Extension = ".enc";

    /// <summary>OWASP's 2023 figure for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>The accepted range of a header's iteration count: below is trivially weak, above is a denial of service.</summary>
    public const int MinIterations = 1_000;
    public const int MaxIterations = 10_000_000;

    public const int DefaultChunkSize = 1024 * 1024;
    public const int MaxChunkSize = 16 * 1024 * 1024;

    internal const int HeaderLength = 40;
    internal const int TagLength = 16;
    internal const int SaltLength = 16;
    internal const int NonceLength = 12;
    internal const int NoncePrefixLength = 8;
    internal const byte FormatVersion = 1;
    internal const byte KdfPbkdf2Sha256 = 1;
    internal const byte CipherAes256Gcm = 1;
    private static readonly byte[] Magic = "ITENC"u8.ToArray();

    /// <summary>PBKDF2 iterations written into new files; tests lower it, nothing else should.</summary>
    public int Iterations { get; init; } = DefaultIterations;

    /// <summary>Plaintext bytes per chunk written into new files.</summary>
    public int ChunkSize { get; init; } = DefaultChunkSize;

    public string TargetPathFor(string source, EncryptionMode mode)
    {
        if (mode == EncryptionMode.Encrypt)
        {
            return source + Extension;
        }

        return source.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) && source.Length > Extension.Length
            ? source[..^Extension.Length]
            : source + ".decrypted";
    }

    public async Task EncryptAsync(Stream input, Stream output, string passphrase, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        if (ChunkSize is < 1 or > MaxChunkSize || Iterations is < MinIterations or > MaxIterations)
        {
            throw new InvalidOperationException("The chunk size or the iteration count is out of range.");
        }

        var header = new byte[HeaderLength];
        Magic.CopyTo(header, 0);
        header[5] = FormatVersion;
        header[6] = KdfPbkdf2Sha256;
        header[7] = CipherAes256Gcm;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)Iterations);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)ChunkSize);
        RandomNumberGenerator.Fill(header.AsSpan(16, SaltLength));
        RandomNumberGenerator.Fill(header.AsSpan(16 + SaltLength, NoncePrefixLength));

        var key = DeriveKey(passphrase, header.AsSpan(16, SaltLength), Iterations);
        var current = new byte[ChunkSize];
        var next = new byte[ChunkSize];
        try
        {
            await output.WriteAsync(header, cancellationToken);
            using var aes = new AesGcm(key, TagLength);
            var sealedChunk = new byte[ChunkSize + TagLength];
            var nonce = new byte[NonceLength];
            var aad = new byte[HeaderLength + 1];
            header.CopyTo(aad, 0);
            header.AsSpan(16 + SaltLength, NoncePrefixLength).CopyTo(nonce);

            var currentLength = await ReadFullAsync(input, current, cancellationToken);
            uint counter = 0;
            long done = 0;
            while (true)
            {
                // Only a full chunk can have more behind it; a short read is the end of the input.
                var nextLength = currentLength == ChunkSize ? await ReadFullAsync(input, next, cancellationToken) : 0;
                var isFinal = nextLength == 0;
                aad[HeaderLength] = isFinal ? (byte)1 : (byte)0;
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixLength), counter);
                aes.Encrypt(nonce, current.AsSpan(0, currentLength), sealedChunk.AsSpan(0, currentLength), sealedChunk.AsSpan(currentLength, TagLength), aad);
                await output.WriteAsync(sealedChunk.AsMemory(0, currentLength + TagLength), cancellationToken);
                done += currentLength;
                progress?.Report(done);
                if (isFinal)
                {
                    break;
                }

                if (counter == uint.MaxValue)
                {
                    throw new InvalidOperationException("The file is too large for one encrypted file.");
                }

                counter++;
                (current, next) = (next, current);
                currentLength = nextLength;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(current);
            CryptographicOperations.ZeroMemory(next);
        }
    }

    public async Task DecryptAsync(Stream input, Stream output, string passphrase, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        var header = new byte[HeaderLength];
        var read = await ReadFullAsync(input, header, cancellationToken);
        var checkedMagic = Math.Min(read, Magic.Length);
        if (!header.AsSpan(0, checkedMagic).SequenceEqual(Magic.AsSpan(0, checkedMagic)) || read == 0)
        {
            throw new EncryptionException(EncryptionFailure.NotEncrypted, "This is not a file encrypted by iTerminal.");
        }

        if (read < HeaderLength)
        {
            throw new EncryptionException(EncryptionFailure.BadHeader, "The file ends inside its header.");
        }

        if (header[5] != FormatVersion || header[6] != KdfPbkdf2Sha256 || header[7] != CipherAes256Gcm)
        {
            throw new EncryptionException(EncryptionFailure.Unsupported, "The file was written by a version or with settings this iTerminal does not support.");
        }

        var iterations = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
        if (iterations is < MinIterations or > MaxIterations || chunkSize is < 1 or > MaxChunkSize)
        {
            throw new EncryptionException(EncryptionFailure.BadHeader, "The file's header holds values that are out of range.");
        }

        var key = DeriveKey(passphrase, header.AsSpan(16, SaltLength), (int)iterations);
        var sealedSize = (int)chunkSize + TagLength;
        var current = new byte[sealedSize];
        var next = new byte[sealedSize];
        var plain = new byte[chunkSize];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            var nonce = new byte[NonceLength];
            var aad = new byte[HeaderLength + 1];
            header.CopyTo(aad, 0);
            header.AsSpan(16 + SaltLength, NoncePrefixLength).CopyTo(nonce);

            var currentLength = await ReadFullAsync(input, current, cancellationToken);
            uint counter = 0;
            long done = 0;
            while (true)
            {
                if (currentLength < TagLength)
                {
                    throw Fail(counter);
                }

                var nextLength = currentLength == sealedSize ? await ReadFullAsync(input, next, cancellationToken) : 0;
                var isFinal = nextLength == 0;
                aad[HeaderLength] = isFinal ? (byte)1 : (byte)0;
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixLength), counter);
                var plainLength = currentLength - TagLength;
                try
                {
                    aes.Decrypt(nonce, current.AsSpan(0, plainLength), current.AsSpan(plainLength, TagLength), plain.AsSpan(0, plainLength), aad);
                }
                catch (CryptographicException)
                {
                    throw Fail(counter);
                }

                await output.WriteAsync(plain.AsMemory(0, plainLength), cancellationToken);
                done += plainLength;
                progress?.Report(done);
                if (isFinal)
                {
                    break;
                }

                counter++;
                (current, next) = (next, current);
                currentLength = nextLength;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static EncryptionException Fail(uint chunk) => chunk == 0
        ? new EncryptionException(EncryptionFailure.WrongPassphraseOrCorrupt, "Wrong passphrase, or the file is damaged.")
        : new EncryptionException(EncryptionFailure.Corrupt, $"The file is damaged or was changed (part {chunk + 1}).");

    public async Task<EncryptionResult> ProcessAsync(
        IReadOnlyList<string> paths,
        EncryptionMode mode,
        string passphrase,
        IProgress<EncryptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var total = paths.Sum(SafeLength);
        long completed = 0;
        var items = new List<EncryptionItem>();
        foreach (string path in paths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new EncryptionResult(items, Cancelled: true);
            }

            var length = SafeLength(path);
            var before = completed;
            var fileProgress = new InlineProgress(bytes => progress?.Report(new EncryptionProgress(before + bytes, total, path)));
            try
            {
                var target = await ProcessOneAsync(path, mode, passphrase, fileProgress, cancellationToken);
                items.Add(new EncryptionItem(path, target, null));
            }
            catch (OperationCanceledException)
            {
                return new EncryptionResult(items, Cancelled: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or EncryptionException or CryptographicException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                items.Add(new EncryptionItem(path, null, exception.Message));
            }

            completed += length;
        }

        return new EncryptionResult(items, Cancelled: false);
    }

    private sealed class InlineProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    private async Task<string> ProcessOneAsync(string path, EncryptionMode mode, string passphrase, IProgress<long> progress, CancellationToken cancellationToken)
    {
        var sourceUrl = new Url(path);
        if (!fileSystem.ExistsFile(sourceUrl))
        {
            throw new IOException("Not a file.");
        }

        var target = TargetPathFor(path, mode);
        var targetUrl = new Url(target);
        if (fileSystem.ExistsFile(targetUrl) || fileSystem.ExistsFolder(targetUrl))
        {
            throw new IOException($"'{Path.GetFileName(target)}' already exists; nothing was overwritten.");
        }

        // Beside the target, so the move is a rename on one volume and a half-written file is never under the final name.
        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        var succeeded = false;
        try
        {
            await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                if (mode == EncryptionMode.Encrypt)
                {
                    await EncryptAsync(input, output, passphrase, progress, cancellationToken);
                }
                else
                {
                    await DecryptAsync(input, output, passphrase, progress, cancellationToken);
                }

                // To the disk, not just to the OS: an original may be deleted right after, and a power loss must not leave neither.
                output.Flush(flushToDisk: true);
            }

            // Never overwrite: the check above can lose a race, the move refuses to replace.
            await fileSystem.MoveAsync(new Url(temp), targetUrl, overwrite: false);
            succeeded = true;
            return target;
        }
        finally
        {
            if (!succeeded)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A temp file that cannot be removed is left behind; it holds only ciphertext or already-authenticated plaintext.
                }
            }
        }
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int iterations)
    {
        string normalised;
        try
        {
            normalised = passphrase.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // A lone surrogate cannot be normalised; the raw text still derives the same key every time.
            normalised = passphrase;
        }

        var password = Encoding.UTF8.GetBytes(normalised);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }

    private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
