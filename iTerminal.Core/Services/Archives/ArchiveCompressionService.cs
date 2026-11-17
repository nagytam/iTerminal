using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Archives;

/// <summary>
/// See <see cref="IArchiveCompressionService"/>. Hands each format to its writer:
/// <see cref="ManagedArchiveWriter"/> for ZIP and the TAR family, <see cref="SevenZipArchiveWriter"/>
/// for 7z when 7-Zip is installed, and <see cref="RarArchiveWriter"/> for RAR when the <c>rar</c> program is.
/// </summary>
public sealed class ArchiveCompressionService(IExternalProcessRunner runner, SevenZipArchiveWriter? sevenZip = null, RarArchiveWriter? rar = null)
    : IArchiveCompressionService
{
    private readonly SevenZipArchiveWriter _sevenZip = sevenZip ?? new SevenZipArchiveWriter(runner);
    private readonly RarArchiveWriter _rar = rar ?? new RarArchiveWriter(runner);

    public IReadOnlyList<ArchiveFormat> AvailableFormats =>
        [.. ArchiveFormat.All.Where(f => WhyUnavailable(f) is null)];

    public string? WhyUnavailable(ArchiveFormat format) => format.Writer switch
    {
        ArchiveWriterKind.SevenZip when !_sevenZip.IsAvailable => "7z needs 7-Zip installed",
        ArchiveWriterKind.RarCli when !_rar.IsAvailable => "RAR needs the rar program installed",
        _ => null,
    };

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
        return options.Format.Writer switch
        {
            ArchiveWriterKind.SevenZip => _sevenZip.WriteAsync(archivePath, items, options, progress, cancellationToken),
            ArchiveWriterKind.RarCli => _rar.WriteAsync(archivePath, items, options, progress, cancellationToken),
            _ => ManagedArchiveWriter.WriteAsync(archivePath, items, options, progress, cancellationToken),
        };
    }
}
