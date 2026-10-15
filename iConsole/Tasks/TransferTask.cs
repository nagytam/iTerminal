namespace iConsole.Tasks;

/// <summary>
/// A <see cref="BackgroundTask"/> that moves a countable number of items and/or bytes (file copy,
/// move, delete) and wants to surface live statistics — item counts, byte counts, and throughput —
/// beyond the base class's single progress percentage. Subclasses call the protected
/// <c>Report*</c>/<c>Add*</c> helpers as work completes; <see cref="ProgressWindow"/>-style UI can
/// check <c>is TransferTask</c> to render the extra fields.
/// </summary>
public abstract class TransferTask : BackgroundTask
{
    private long _itemsProcessed;
    private long _totalItems;
    private long _bytesProcessed;
    private long _totalBytes;

    public long ItemsProcessed => Interlocked.Read(ref _itemsProcessed);
    public long TotalItems => Interlocked.Read(ref _totalItems);
    public long BytesProcessed => Interlocked.Read(ref _bytesProcessed);
    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    public double ItemsPerSecond => ComputeRate(ItemsProcessed);
    public double BytesPerSecond => ComputeRate(BytesProcessed);

    private double ComputeRate(long amount)
    {
        var elapsedSeconds = Stopwatch.Elapsed.TotalSeconds;
        return elapsedSeconds > 0.001 ? amount / elapsedSeconds : 0;
    }

    protected void ReportItems(long processed, long total)
    {
        Interlocked.Exchange(ref _itemsProcessed, processed);
        Interlocked.Exchange(ref _totalItems, total);
        OnPropertyChanged(nameof(ItemsProcessed));
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(ItemsPerSecond));
    }

    protected void ReportBytes(long processed, long total)
    {
        Interlocked.Exchange(ref _bytesProcessed, processed);
        Interlocked.Exchange(ref _totalBytes, total);
        OnPropertyChanged(nameof(BytesProcessed));
        OnPropertyChanged(nameof(TotalBytes));
        OnPropertyChanged(nameof(BytesPerSecond));
    }

    protected void AddBytesProcessed(long delta)
    {
        Interlocked.Add(ref _bytesProcessed, delta);
        OnPropertyChanged(nameof(BytesProcessed));
        OnPropertyChanged(nameof(BytesPerSecond));
    }

    protected void AddTotalBytes(long delta)
    {
        Interlocked.Add(ref _totalBytes, delta);
        OnPropertyChanged(nameof(TotalBytes));
    }
}
