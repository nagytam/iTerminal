using iConsole.Data;
using iConsole.Localization;
using iConsole.Services;

namespace iTerminal.Core.Services;

/// <summary>
/// Decides one file-conflict at a time for a single run (one dialog or paste, which may be a batch of
/// file steps). The "apply to all" answer is kept for the rest of the run. Prompts are serialised.
/// Choosing Cancel invokes <see cref="CancelRun"/> — the batch or the single task — and throws
/// <see cref="OperationCanceledException"/>, so the step ends cancelled and nothing else runs.
/// </summary>
public sealed class FileConflictResolver(FileConflictMode mode, IConflictPromptService prompts)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileConflictChoice? _applyToAll;

    public FileConflictMode Mode => mode;

    /// <summary>Set by the caller once the batch or task that Cancel should stop exists.</summary>
    public Action? CancelRun { get; set; }

    /// <summary>
    /// Returns what to do about <paramref name="targetUrl"/>, which exists. <paramref name="exists"/>
    /// tells whether a candidate name is taken. Throws for <see cref="FileConflictMode.Legacy"/>
    /// (the error paste has always given) and for a Cancel answer.
    /// </summary>
    public async Task<FileConflictDecision> DecideAsync(Url sourceUrl, Url targetUrl, Func<Url, bool> exists, CancellationToken cancellationToken)
    {
        if (mode == FileConflictMode.Legacy)
        {
            throw new IOException($"The file '{targetUrl.FullName}' already exists.");
        }
        if (mode == FileConflictMode.OverwriteAll)
        {
            return new FileConflictDecision(Skip: false, targetUrl, Replace: true);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                FileConflictChoice choice;
                if (_applyToAll is { } sticky)
                {
                    choice = sticky;
                }
                else
                {
                    var answer = await prompts.ChooseAsync(
                        UiText.Get("Conflict.Title", "File already exists"),
                        string.Format(UiText.Get("Conflict.Message", "{0} is already in the target folder."), targetUrl.Name),
                        cancellationToken);
                    choice = answer.Choice;
                    if (answer.ApplyToAll && choice != FileConflictChoice.Cancel)
                    {
                        _applyToAll = choice;
                    }
                }

                if (choice == FileConflictChoice.Cancel)
                {
                    CancelRun?.Invoke();
                    throw new OperationCanceledException("Cancelled at a file conflict.");
                }

                var decision = FileConflictPolicy.Decide(choice, sourceUrl, targetUrl, exists);
                if (decision is not null)
                {
                    return decision;
                }

                // Keep newer / larger could not tell the two apart: the answer does not carry over.
                _applyToAll = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
