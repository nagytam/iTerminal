using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Archives;

/// <summary>
/// See <see cref="IArchiveCompressionService"/>. Hands each format to its writer:
/// <see cref="ManagedArchiveWriter"/> for ZIP, the TAR family, and the single-file GZIP and BZIP2
/// streams; <see cref="SevenZipArchiveWriter"/> for 7z and <see cref="SevenZipStreamWriter"/> for XZ
/// and TAR.XZ, both when 7-Zip is installed.
/// </summary>
public sealed class ArchiveCompressionService(IExternalProcessRunner runner, SevenZipArchiveWriter? sevenZip = null)
    : IArchiveCompressionService
{
    private readonly SevenZipArchiveWriter _sevenZip = sevenZip ?? new SevenZipArchiveWriter(runner);

    // Its own writer when none was given: both look 7-Zip up on their own, so a test's stub reaches both only when it is passed in.
    private readonly SevenZipStreamWriter _sevenZipStream = new(runner, sevenZip ?? new SevenZipArchiveWriter(runner));

    public IReadOnlyList<ArchiveFormat> AvailableFormats =>
        [.. ArchiveFormat.All.Where(f => WhyUnavailable(f) is null)];

    public string? WhyUnavailable(ArchiveFormat format) =>
        (format.Writer is ArchiveWriterKind.SevenZip or ArchiveWriterKind.SevenZipStream) && !_sevenZip.IsAvailable
            ? $"{format.Name} needs 7-Zip installed"
            : null;

    public Task<CompressionResult> CompressAsync(
        string archivePath,
        IReadOnlyList<ArchiveItem> items,
        CompressionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (WhyUnavailable(options.Format) is { } reason)
        {
            throw new InvalidOperationException(reason + ".");
        }

        if (options.HasPassword && !options.Format.SupportsPassword)
        {
            throw new NotSupportedException($"{options.Format.Name} archives cannot be password-protected.");
        }

        if (options.Format.OneFileOnly && (items.Count != 1 || items[0].IsFolder))
        {
            throw new NotSupportedException($"{options.Format.Name} holds one file: compress one file, or choose an archive format.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(archivePath))!);
        return options.Format.Writer switch
        {
            ArchiveWriterKind.Managed => ManagedArchiveWriter.WriteAsync(archivePath, items, options, progress, cancellationToken),
            ArchiveWriterKind.SingleStream => ManagedArchiveWriter.WriteSingleStreamAsync(archivePath, items, options, progress, cancellationToken),
            ArchiveWriterKind.SevenZip => _sevenZip.WriteAsync(archivePath, items, options, progress, cancellationToken),
            ArchiveWriterKind.SevenZipStream => _sevenZipStream.WriteAsync(archivePath, items, options, progress, cancellationToken),
            _ => throw new NotSupportedException($"No writer for {options.Format.Name}."),
        };
    }
}
