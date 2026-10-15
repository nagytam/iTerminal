namespace iTerminal.Core.Services.Clipboard;

/// <summary>
/// The system clipboard when it works and an in-process one when it does not (no clipboard tool on
/// this Linux, a clipboard another program will not let go of). Once the system clipboard has failed
/// the in-process one answers for the rest of the run, so a copy and the paste that follows it agree.
/// </summary>
public sealed class FallbackFileClipboardService(IFileClipboardService primary, IFileClipboardService fallback) : IFileClipboardService
{
    private volatile bool _primaryFailed;

    public async Task SetFilesAsync(IReadOnlyList<string> paths, bool isCut, CancellationToken cancellationToken = default)
    {
        if (!_primaryFailed)
        {
            try
            {
                await primary.SetFilesAsync(paths, isCut, cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _primaryFailed = true;
            }
        }

        await fallback.SetFilesAsync(paths, isCut, cancellationToken);
    }

    public async Task<FileClipboardContent?> GetFilesAsync(CancellationToken cancellationToken = default)
    {
        if (!_primaryFailed)
        {
            try
            {
                return await primary.GetFilesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _primaryFailed = true;
            }
        }

        return await fallback.GetFilesAsync(cancellationToken);
    }
}
