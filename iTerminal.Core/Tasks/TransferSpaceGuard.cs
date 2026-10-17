using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Checks, before a copy or move writes anything, that the target has room for what is left to copy. When
/// it does not, the user is warned and can pause the operation — to free some space — and resume
/// it, or copy on regardless. Runs on the task's thread; one prompt at a time, so concurrent copy
/// workers wait here while one question is open instead of stacking dialogs.
/// </summary>
internal sealed class TransferSpaceGuard(IFileSystem target, Url targetUrl, IErrorPromptService? prompts)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _proceedAnyway;

    /// <summary>
    /// A guard for a transfer from <paramref name="sourceUrl"/> to <paramref name="targetUrl"/>, or
    /// null when none is needed: a move within one volume only renames, so it always fits.
    /// </summary>
    public static TransferSpaceGuard? For(
        IFileSystem source, Url sourceUrl, IFileSystem target, Url targetUrl, bool deletesSource, IErrorPromptService? prompts)
    {
        if (prompts is null || target is not IFreeSpaceFileSystem space)
        {
            return null;
        }

        if (deletesSource && ReferenceEquals(source, target))
        {
            var sourceVolume = space.GetVolumeId(sourceUrl);
            if (sourceVolume is null || sourceVolume == space.GetVolumeId(targetUrl))
            {
                return null;
            }
        }

        return new TransferSpaceGuard(target, targetUrl, prompts);
    }

    /// <summary>Free bytes on the target, or null when it cannot say (or when no prompts are wired up).</summary>
    public long? FreeBytes() => prompts is null ? null : (target as IFreeSpaceFileSystem)?.GetFreeBytes(targetUrl);

    /// <summary>
    /// Returns once <paramref name="requiredBytes"/> fit on the target, or the user chose to copy
    /// anyway. Throws <see cref="OperationCanceledException"/> when the user cancels at the resume
    /// prompt, or <paramref name="cancellationToken"/> fires.
    /// </summary>
    public async Task EnsureAsync(Func<long> requiredBytes, CancellationToken cancellationToken)
    {
        if (_proceedAnyway || !IsShort(requiredBytes(), out _))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            while (!_proceedAnyway)
            {
                var required = requiredBytes();
                if (!IsShort(required, out var free))
                {
                    return;
                }

                var pause = await prompts!.ConfirmAsync(
                    "Not enough free space",
                    $"The target has {free.ToFileSize()} free, but {required.ToFileSize()} are to be written" +
                    $"{Environment.NewLine}({(required - free).ToFileSize()} short) to:{Environment.NewLine}{targetUrl.FullName.Shrink(66)}" +
                    $"{Environment.NewLine}{Environment.NewLine}Pause the operation to free some space, or go on anyway?",
                    "Pause", "Go on anyway", cancellationToken);
                if (!pause)
                {
                    _proceedAnyway = true;
                    return;
                }

                var resume = await prompts.ConfirmAsync(
                    "Copy paused",
                    $"The operation is paused.{Environment.NewLine}Free some space on the target, then resume." +
                    $"{Environment.NewLine}{Environment.NewLine}{targetUrl.FullName.Shrink(66)}",
                    "Resume", "Cancel", cancellationToken);
                if (!resume)
                {
                    throw new OperationCanceledException("Cancelled while paused for lack of space.");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsShort(long required, out long free)
    {
        free = FreeBytes() ?? long.MaxValue;
        return free < required;
    }
}
