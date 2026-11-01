namespace iConsole.Services;

/// <summary>
/// Asks, from any thread, what to do about a file that already exists at a copy or move's target.
/// Returns the chosen answer and whether it applies to the rest of the run. Esc in the prompt is
/// <c>Cancel</c>. Throws <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/>
/// fires before the user answers.
/// </summary>
public interface IConflictPromptService
{
    Task<(FileConflictChoice Choice, bool ApplyToAll)> ChooseAsync(string title, string message, CancellationToken cancellationToken);
}

/// <summary>What the user answered in a file-conflict prompt.</summary>
public enum FileConflictChoice
{
    Overwrite,
    Skip,
    Rename,
    KeepNewer,
    KeepLarger,
    Cancel,
}
