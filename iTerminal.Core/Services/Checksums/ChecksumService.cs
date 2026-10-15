using iConsole.Data;
using iConsole.Interfaces;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.Checksums;

public enum ChecksumAlgorithm
{
    Md5,
    Sha1,
    Sha256,
    Sha512,
}

/// <summary>Every checksum of one file, as lower-case hex, and how many bytes went into them.</summary>
public sealed record ChecksumResult(IReadOnlyDictionary<ChecksumAlgorithm, string> Hashes, long Bytes);

/// <summary>A checksum found written down for a file: the value, the algorithm its length implies, and where it was found.</summary>
public sealed record ExpectedChecksum(ChecksumAlgorithm Algorithm, string Hash, string Source);

/// <summary>
/// Computes the checksums of a file on any drive. Async by contract: a file is read from start to
/// end, which for a large one — or one behind ssh or inside a container — is seconds to minutes.
/// </summary>
public interface IChecksumService
{
    /// <param name="progress">Reports the bytes read so far.</param>
    Task<ChecksumResult> ComputeAsync(Url url, IFileSystem fileSystem, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}

public partial class ChecksumService : IChecksumService
{
    public static readonly IReadOnlyList<ChecksumAlgorithm> Algorithms =
        [ChecksumAlgorithm.Md5, ChecksumAlgorithm.Sha1, ChecksumAlgorithm.Sha256, ChecksumAlgorithm.Sha512];

    public static string DisplayName(ChecksumAlgorithm algorithm) => algorithm switch
    {
        ChecksumAlgorithm.Md5 => "MD5",
        ChecksumAlgorithm.Sha1 => "SHA-1",
        ChecksumAlgorithm.Sha256 => "SHA-256",
        _ => "SHA-512",
    };

    /// <summary>The extension of the file a checksum is conventionally written to: <c>name.iso.sha256</c>.</summary>
    public static string Extension(ChecksumAlgorithm algorithm) => algorithm switch
    {
        ChecksumAlgorithm.Md5 => ".md5",
        ChecksumAlgorithm.Sha1 => ".sha1",
        ChecksumAlgorithm.Sha256 => ".sha256",
        _ => ".sha512",
    };

    private static HashAlgorithmName NameOf(ChecksumAlgorithm algorithm) => algorithm switch
    {
        ChecksumAlgorithm.Md5 => HashAlgorithmName.MD5,
        ChecksumAlgorithm.Sha1 => HashAlgorithmName.SHA1,
        ChecksumAlgorithm.Sha256 => HashAlgorithmName.SHA256,
        _ => HashAlgorithmName.SHA512,
    };

    /// <summary>
    /// All the algorithms in one pass over the file: reading is what takes the time, and a second
    /// read for a second algorithm would double it — over ssh, double the transfer.
    /// </summary>
    public async Task<ChecksumResult> ComputeAsync(Url url, IFileSystem fileSystem, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var hashes = Algorithms.ToDictionary(algorithm => algorithm, algorithm => IncrementalHash.CreateHash(NameOf(algorithm)));
        try
        {
            long total = 0;
            void Append(ReadOnlySpan<byte> data)
            {
                foreach (var hash in hashes.Values)
                {
                    hash.AppendData(data);
                }

                total += data.Length;
                progress?.Report(total);
            }

            if (url.ContainerType == ContainerType.FileSystem)
            {
                await using var file = new FileStream(url.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    Append(buffer.AsSpan(0, read));
                }
            }
            else
            {
                ArgumentNullException.ThrowIfNull(fileSystem);
                await fileSystem.StreamToAsync(url, new HashingStream(Append, cancellationToken), null, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ChecksumResult(
                hashes.ToDictionary(pair => pair.Key, pair => Convert.ToHexStringLower(pair.Value.GetHashAndReset())),
                total);
        }
        finally
        {
            foreach (var hash in hashes.Values)
            {
                hash.Dispose();
            }
        }
    }

    #region Reading a checksum someone wrote down
    /// <summary>The algorithm a hex string of this length is a checksum of, or null when it is none of them.</summary>
    public static ChecksumAlgorithm? AlgorithmFor(string? hash)
    {
        var text = Normalize(hash);
        if (text.Length == 0 || !text.All(Uri.IsHexDigit))
        {
            return null;
        }

        return text.Length switch
        {
            32 => ChecksumAlgorithm.Md5,
            40 => ChecksumAlgorithm.Sha1,
            64 => ChecksumAlgorithm.Sha256,
            128 => ChecksumAlgorithm.Sha512,
            _ => null,
        };
    }

    /// <summary>A checksum as typed or pasted: blanks and the separators some tools print between bytes removed, lower case.</summary>
    public static string Normalize(string? hash) =>
        new string([.. (hash ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != ':' && c != '-')]).ToLowerInvariant();

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{32,128})\s+[* ]?(?<name>.+?)\s*$")]
    private static partial Regex GnuLine();

    [GeneratedRegex(@"^[A-Za-z0-9-]+\s*\((?<name>.+)\)\s*=\s*(?<hash>[0-9a-fA-F]{32,128})\s*$")]
    private static partial Regex BsdLine();

    /// <summary>
    /// The checksum <paramref name="text"/> — the contents of a checksum file — gives for
    /// <paramref name="fileName"/>, or null. Reads what the common tools write:
    /// <c>hash  name</c> and <c>hash *name</c> (sha256sum, md5sum), <c>SHA256 (name) = hash</c>
    /// (BSD, openssl), and a file holding nothing but the hash.
    /// </summary>
    public static string? ParseChecksumFile(string? text, string fileName)
    {
        var lines = (text ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim().TrimStart('﻿'))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        foreach (var line in lines)
        {
            var match = GnuLine().Match(line);
            if (!match.Success)
            {
                match = BsdLine().Match(line);
            }

            if (match.Success && NamesTheFile(match.Groups["name"].Value, fileName) && AlgorithmFor(match.Groups["hash"].Value) is not null)
            {
                return Normalize(match.Groups["hash"].Value);
            }
        }

        // A file that is just the hash: only when it is the single thing in it, or a list of
        // other files' checksums would be read as this file's.
        return lines.Count == 1 && AlgorithmFor(lines[0]) is not null ? Normalize(lines[0]) : null;
    }

    /// <summary>A list names files as they were when it was written — often with a folder in front (<c>./dist/app.zip</c>).</summary>
    private static bool NamesTheFile(string listed, string fileName)
    {
        var name = listed.Trim().Replace('\\', '/');
        var cut = name.LastIndexOf('/');
        return string.Equals(cut >= 0 ? name[(cut + 1)..] : name, fileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A checksum written down beside the local file at <paramref name="filePath"/>: in
    /// <c>name.sha256</c> (or <c>.md5</c>, <c>.sha1</c>, <c>.sha512</c>), or in the folder's
    /// <c>SHA256SUMS</c>-style list. Null when there is none — or none that mentions this file.
    /// </summary>
    public static ExpectedChecksum? FindWrittenChecksum(string filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        var name = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        var candidates = new List<string>();
        foreach (var algorithm in Algorithms.Reverse())
        {
            candidates.Add(name + Extension(algorithm));
        }

        foreach (var algorithm in Algorithms.Reverse())
        {
            var list = DisplayName(algorithm).Replace("-", string.Empty) + "SUMS";
            candidates.AddRange([list, list + ".txt", list.ToLowerInvariant(), list.ToLowerInvariant() + ".txt"]);
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var path = Path.Combine(folder, candidate);
                // A list of checksums is a few kilobytes; anything large under one of these names
                // is something else, and not worth reading on the way to a dialog.
                if (!File.Exists(path) || new FileInfo(path).Length > 1 << 20)
                {
                    continue;
                }

                if (ParseChecksumFile(File.ReadAllText(path), name) is { } hash && AlgorithmFor(hash) is { } algorithm)
                {
                    return new ExpectedChecksum(algorithm, hash, candidate);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    /// <summary>The line the common tools write and read: the hash, a blank, <c>*</c> for "binary", the name.</summary>
    public static string FormatChecksumLine(string hash, string fileName) => $"{hash} *{fileName}";
    #endregion

    /// <summary>A write-only stream that hands what is written to it to the hashes — for drives that can only stream a file out.</summary>
    private sealed class HashingStream(HashingStream.Sink sink, CancellationToken cancellationToken) : Stream
    {
        public delegate void Sink(ReadOnlySpan<byte> data);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sink(buffer);
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
