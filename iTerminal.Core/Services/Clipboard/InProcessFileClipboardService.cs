namespace iTerminal.Core.Services.Clipboard;

/// <summary>
/// A clipboard that lives in this process only: used where the system one cannot be reached (macOS,
/// a Linux box with neither wl-clipboard nor xclip) and by the end-to-end suite, where a test must
/// never overwrite what the person running it had copied.
/// </summary>
public sealed class InProcessFileClipboardService : IFileClipboardService
{
    private readonly object _lock = new();
    private FileClipboardContent? _content;

    public Task SetFilesAsync(IReadOnlyList<string> paths, bool isCut, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _content = new FileClipboardContent([.. paths], isCut);
        }

        return Task.CompletedTask;
    }

    public Task<FileClipboardContent?> GetFilesAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_content);
        }
    }
}
