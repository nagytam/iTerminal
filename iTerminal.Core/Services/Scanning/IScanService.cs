namespace iTerminal.Core.Services.Scanning;

/// <summary>
/// Scanners for the scan dialog: list the devices, scan one page, save it. Every member is async and runs off the
/// render thread; a scanner problem comes back as a sentence in the result, never as an exception. Only a
/// caller's own cancellation is thrown.
/// </summary>
public interface IScanService
{
    /// <summary>The scanners the platform reports.</summary>
    Task<ScannerListing> ListScannersAsync(CancellationToken cancellationToken = default);

    /// <summary>Scans one page from <paramref name="scanner"/> with <paramref name="options"/>.</summary>
    Task<ScanResult> ScanAsync(ScannerDevice scanner, ScanOptions options, CancellationToken cancellationToken = default);

    /// <summary>Writes a scanned page to <paramref name="filePath"/> in <paramref name="format"/>.</summary>
    Task<SaveResult> SaveAsync(ScanPage page, string filePath, ScanFileFormat format, CancellationToken cancellationToken = default);
}

/// <summary>The platform side of scanning: TWAIN on Windows, SANE elsewhere. <see cref="ScanService"/> wraps one.</summary>
public interface IScannerBackend
{
    Task<ScannerListing> ListAsync(CancellationToken cancellationToken);

    Task<ScanResult> ScanAsync(ScannerDevice scanner, ScanOptions options, CancellationToken cancellationToken);
}

public sealed class ScanService(IScannerBackend backend) : IScanService
{
    public Task<ScannerListing> ListScannersAsync(CancellationToken cancellationToken = default) =>
        backend.ListAsync(cancellationToken);

    public Task<ScanResult> ScanAsync(ScannerDevice scanner, ScanOptions options, CancellationToken cancellationToken = default) =>
        backend.ScanAsync(scanner, options, cancellationToken);

    public async Task<SaveResult> SaveAsync(ScanPage page, string filePath, ScanFileFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            byte[] encoded = await Task.Run(() => ScanImageCodec.Encode(page, format), cancellationToken);
            await File.WriteAllBytesAsync(filePath, encoded, cancellationToken);
            return new SaveResult(true, $"Saved {Path.GetFileName(filePath)}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new SaveResult(false, ex.Message);
        }
    }
}
