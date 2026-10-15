namespace iConsole.Services;

/// <summary>
/// Shows an interactive Continue/Cancel prompt from any thread — including a background
/// operation's worker thread, not just the input-handling flow — and awaits the user's choice.
/// Used by batch pipelines (folder copy/move/delete) to ask whether to skip a failing item and
/// keep going, or abort the rest of the operation.
/// </summary>
public interface IErrorPromptService
{
    /// <summary>
    /// Returns true if the user chose to continue (skip the failing item), false if they chose to
    /// cancel (abort the rest of the operation). Throws <see cref="OperationCanceledException"/> if
    /// <paramref name="cancellationToken"/> fires before the user responds.
    /// </summary>
    Task<bool> ConfirmContinueAsync(string title, string message, CancellationToken cancellationToken);
}
