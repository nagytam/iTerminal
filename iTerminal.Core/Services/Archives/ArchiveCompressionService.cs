using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Archives;

/// <summary>
/// See <see cref="IArchiveCompressionService"/>. Hands each format to its writer:
/// <see cref="ManagedArchiveWriter"/> for ZIP and the TAR family, <see cref="SevenZipArchiveWriter"/>
/// for 7z when 7-Zip is installed.
/// </summary>
public sealed class ArchiveCompressionService(IExternalProcessRunner runner, SevenZipArchiveWriter? sevenZip = null)
    : IArchiveCompressionService
{
    private readonly SevenZipArchiveWriter _sevenZip = sevenZip ?? new SevenZipArchiveWriter(runner);

    public IReadOnlyList<ArchiveFormat> AvailableFormats =>
        [.. ArchiveFormat.All.Where(f => WhyUnavailable(f) is null)];

    public string? WhyUnavailable(ArchiveFormat format) =>
        format.Writer == ArchiveWriterKind.SevenZip && !_sevenZip.IsAvailable
            ? "7z needs 7-Zip installed"
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

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(archivePath))!);
        return options.Format.Writer == ArchiveWriterKind.SevenZip
            ? _sevenZip.WriteAsync(archivePath, items, options, progress, cancellationToken)
            : ManagedArchiveWriter.WriteAsync(archivePath, items, options, progress, cancellationToken);
    }
}
