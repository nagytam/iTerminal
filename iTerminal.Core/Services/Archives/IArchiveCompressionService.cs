namespace iTerminal.Core.Services.Archives;

/// <summary>
/// One file or folder to put in an archive.
/// </summary>
/// <param name="EntryName">The path inside the archive, '/'-separated: <c>photos/2024/a.jpg</c>. A folder ends in '/'.</param>
/// <param name="Size">The file's length; only a hint (a remote listing may not know it).</param>
/// <param name="LocalPath">The file on the local disk, when it is one — read directly, and what 7-Zip is handed.</param>
/// <param name="CopyToAsync">Writes the file's bytes, for a file on any drive. Null for a folder.</param>
public sealed record ArchiveItem(
    string EntryName,
    bool IsFolder,
    long Size,
    DateTime Modified,
    string? LocalPath = null,
    Func<Stream, CancellationToken, Task>? CopyToAsync = null)
{
    /// <summary>Copies the file's bytes into <paramref name="destination"/>.</summary>
    public async Task WriteToAsync(Stream destination, CancellationToken cancellationToken)
    {
        if (LocalPath is not null)
        {
            await using var file = new FileStream(LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true);
            await file.CopyToAsync(destination, cancellationToken);
        }
        else if (CopyToAsync is not null)
        {
            await CopyToAsync(destination, cancellationToken);
        }
    }
}

/// <summary>What the Compress dialog asked for.</summary>
/// <param name="Password">Null or empty for no password.</param>
/// <param name="EncryptNames">7z only: hide the file names behind the password too.</param>
/// <param name="ReplaceExisting">
/// When adding to an existing archive: whether a file already in it under the same name is
/// replaced (true) or kept, the new one being skipped (false).
/// </param>
public sealed record CompressionOptions(
    ArchiveFormat Format,
    int Level,
    string? Password = null,
    ArchiveEncryption Encryption = ArchiveEncryption.Aes256,
    bool EncryptNames = false,
    bool ReplaceExisting = true)
{
    public bool HasPassword => !string.IsNullOrEmpty(Password);
}

/// <summary>How far a compression has got.</summary>
/// <param name="CurrentEntry">The entry being written, when the writer knows.</param>
public sealed record ArchiveProgress(long BytesDone, long BytesTotal, int ItemsDone, int ItemsTotal, string CurrentEntry);

/// <summary>What a finished compression did.</summary>
/// <param name="Added">New entries written.</param>
/// <param name="Kept">Entries carried over from the archive that was there before.</param>
/// <param name="Skipped">New files left out because the archive already had them and replacing was off.</param>
public sealed record CompressionResult(int Added, int Kept, int Skipped);

/// <summary>
/// Writes archives: creates one, or adds to one that exists. The archive is always a local file —
/// <see cref="Tasks.CompressTask"/> brings a remote one down and puts it back. Never touches the
/// archive until the new one is complete: the result is written next to it and moved over it, so
/// a failure or a Cancel leaves the old archive exactly as it was.
/// </summary>
public interface IArchiveCompressionService
{
    /// <summary>The formats that can be written on this machine (7z only when 7-Zip is installed).</summary>
    IReadOnlyList<ArchiveFormat> AvailableFormats { get; }

    /// <summary>Why a format is missing from <see cref="AvailableFormats"/>, or null.</summary>
    string? WhyUnavailable(ArchiveFormat format);

    Task<CompressionResult> CompressAsync(
        string archivePath,
        IReadOnlyList<ArchiveItem> items,
        CompressionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken);
}
