using iConsole.Localization;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Helper;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Shared engine for transferring a single file between two <see cref="IFileSystem"/>
/// implementations (e.g. a real disk and the memory drive). When <paramref name="source"/> and
/// <paramref name="target"/> are the same drive it delegates to the drive's native operation via
/// <see cref="SameFileSystemTransferAsync"/>; otherwise it streams the content through a
/// <see cref="System.IO.Pipelines.Pipe"/> via <see cref="PipelinedFileCopyHelper"/> rather than
/// buffering the whole file in memory, so it works regardless of which drive types are involved.
/// Copy and move differ only in the verb, the native fast path, and whether the source is deleted
/// afterwards (<see cref="DeletesSource"/>).
/// </summary>
public abstract class ContentModelFileTransferTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url requestedTargetUrl, FileConflictResolver resolver, IErrorPromptService? errorPromptService = null, TransferVerifier? verifier = null) : BackgroundTask
{
    private Url _targetUrl = requestedTargetUrl;

    /// <summary>Verb for task descriptions, e.g. "Copy".</summary>
    protected abstract string Verb { get; }

    /// <summary>Progressive verb for streaming updates, e.g. "Copying".</summary>
    protected abstract string ProgressVerb { get; }

    /// <summary>True when the source file is removed after a successful transfer (move).</summary>
    protected abstract bool DeletesSource { get; }

    /// <summary>Native fast path used when source and target are the same drive.</summary>
    protected abstract Task SameFileSystemTransferAsync(IFileSystem fileSystem, Url sourceUrl, Url targetUrl, bool overwrite);

    public override async Task ExecuteAsync()
    {
        try
        {
            // A file sent to a folder goes inside it under its own name, as in every commander, rather
            // than being written to a path that is the folder's. Asked here, on the task's thread: on a
            // remote drive it is a round trip.
            var asFolder = new Url(UrlPath.TrimFolder(_targetUrl.FullUrl) + UrlPath.SeparatorOf(_targetUrl.FullUrl));
            if (await target.ExistsFolderAsync(asFolder, CancellationToken).ConfigureAwait(false) && !await target.ExistsFileAsync(_targetUrl, CancellationToken).ConfigureAwait(false))
            {
                _targetUrl = new Url(UrlPath.Child(_targetUrl.FullUrl, FocusedEntry.NameOf(sourceUrl), isFolder: false));
            }

            // A file sent onto itself (same folder, same name) is not a conflict to ask about: overwriting a file with
            // itself either fails or does nothing. Said plainly, before the prompt. Exact names only, so a move that only
            // changes the case of a name (a rename on a case-insensitive drive) still goes through.
            if (ReferenceEquals(source, target) && IsSameFile(sourceUrl, _targetUrl))
            {
                throw new IOException(UiText.Get("Transfer.SameFile", "The source and the target are the same file. Choose another name or folder."));
            }

            Subject = TaskNotificationHelper.TransferSubject(sourceUrl, _targetUrl);
            Description = string.Join(Environment.NewLine, string.Format(UiText.Get("Task.VerbFile", "{0} file"), Verb), sourceUrl.FullName.Shrink(60), UiText.Get("Task.To", "to"), _targetUrl.FullName.Shrink(60), UiText.Get("Task.Started", "started"));

            if (target.IsReadOnly)
            {
                throw new NotSupportedException($"Cannot {Verb.ToLowerInvariant()} to a read-only destination.");
            }
            if (DeletesSource && source.IsReadOnly)
            {
                throw new NotSupportedException($"Cannot {Verb.ToLowerInvariant()} from a read-only source; use copy instead.");
            }

            // A conflict is decided before anything is reserved or written: a skipped file neither
            // triggers the space warning nor touches the target.
            var replace = false;
            if (await target.ExistsFileAsync(_targetUrl, CancellationToken).ConfigureAwait(false))
            {
                // The resolver asks by name, several times, so it keeps the synchronous check (a Func<Url, bool>).
                var decision = await resolver.DecideAsync(sourceUrl, _targetUrl, target.ExistsFile, CancellationToken);
                if (decision.Skip)
                {
                    Description = string.Join(Environment.NewLine, UiText.Get("Task.Skipped", "Skipped"), sourceUrl.FullName.Shrink(60), UiText.Get("Task.To", "to"), _targetUrl.FullName.Shrink(60));
                    ProgressPercentage = 100;
                    return;
                }

                _targetUrl = decision.Target;
                replace = decision.Replace;
            }

            if (sourceUrl.Length > 0)
            {
                // Before anything is written: a file that does not fit warns instead of failing half way.
                var spaceGuard = TransferSpaceGuard.For(source, sourceUrl, target, _targetUrl, DeletesSource && verifier is null, errorPromptService);
                if (spaceGuard is not null)
                {
                    await spaceGuard.EnsureAsync(() => sourceUrl.Length, CancellationToken);
                }
            }

            // With verification the bytes have to go through a copy that can be checked, so a one-drive
            // transfer is streamed too (a move is then copy, check, delete instead of a rename) - except onto
            // itself, where streaming would truncate the file it is reading and there is nothing to compare.
            if (ReferenceEquals(source, target) && (verifier is null || UrlPath.SameOrInside(sourceUrl, _targetUrl)))
            {
                await SameFileSystemTransferAsync(source, sourceUrl, _targetUrl, replace);
            }
            else
            {
                var totalBytes = sourceUrl.Length;
                var progress = new SynchronousProgress<long>(bytesCopied =>
                {
                    if (totalBytes > 0)
                    {
                        OnProgressChanged(Math.Clamp(100L * bytesCopied / totalBytes, 0, 100));
                        Description = string.Join(Environment.NewLine, ProgressVerb, sourceUrl.Name, $"{bytesCopied.ToFileSize()} / {totalBytes.ToFileSize()}");
                    }
                    else
                    {
                        Description = string.Join(Environment.NewLine, ProgressVerb, sourceUrl.Name, string.Format(UiText.Get("Task.BytesCopied", "{0} copied"), bytesCopied.ToFileSize()));
                    }
                });

                try
                {
                    await PipelinedFileCopyHelper.CopyAsync(source, target, sourceUrl, _targetUrl, progress, CancellationToken, PauseGate);
                }
                catch (OperationCanceledException)
                {
                    await TryDeletePartialTargetAsync().ConfigureAwait(false);
                    throw;
                }

                if (verifier is not null)
                {
                    // Outside the try above: a cancel now leaves a complete copy, not a partial one to remove.
                    // A mismatch throws before the source is touched, and the bad copy stays.
                    var verifyProgress = new SynchronousProgress<long>(bytesRead =>
                        Description = string.Join(Environment.NewLine, UiText.Get("Verb.Verifying", "Verifying"), sourceUrl.Name, bytesRead.ToFileSize()));
                    Description = string.Join(Environment.NewLine, UiText.Get("Verb.Verifying", "Verifying"), sourceUrl.Name);
                    await verifier.VerifyAsync(source, target, sourceUrl, _targetUrl, verifyProgress, CancellationToken);
                }

                if (DeletesSource)
                {
                    // The copy is complete and verified, so this runs without the token: a cancel must not leave both files.
                    await source.DeleteFileAsync(sourceUrl, moveToRecycleBin: false, CancellationToken.None).ConfigureAwait(false);
                }
            }

            Description = string.Join(Environment.NewLine, string.Format(UiText.Get("Task.VerbFile", "{0} file"), Verb), sourceUrl.FullName.Shrink(60), UiText.Get("Task.To", "to"), _targetUrl.FullName.Shrink(60), UiText.Get("Task.Finished", "finished"));
            ProgressPercentage = 100;
        }
        catch (OperationCanceledException)
        {
            Description = string.Join(Environment.NewLine, string.Format(UiText.Get("Task.VerbFile", "{0} file"), Verb), sourceUrl.FullName.Shrink(60), UiText.Get("Task.Cancelled", "cancelled"));
        }
        catch (Exception ex)
        {
            var failure = TaskNotificationHelper.FailureText(ex);
            Description = string.Format(UiText.Get("Task.VerbFileFailed", "{0} file failed: {1}"), Verb, failure);
            Error = failure;
        }
    }

    /// <summary>True when both URLs name the same path exactly (either separator, trailing separator ignored).</summary>
    internal static bool IsSameFile(Url first, Url second)
    {
        static string Norm(string? url) => UrlPath.TrimFolder(url).Replace('\\', '/');
        return string.Equals(Norm(first.FullUrl), Norm(second.FullUrl), StringComparison.Ordinal);
    }

    // The cleanup runs after a cancel, so it takes no token: it must still run once the task's own token is cancelled.
    private async Task TryDeletePartialTargetAsync()
    {
        try
        {
            await target.DeleteFileAsync(_targetUrl, moveToRecycleBin: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup only — the transfer already failed/was cancelled.
        }
    }
}
