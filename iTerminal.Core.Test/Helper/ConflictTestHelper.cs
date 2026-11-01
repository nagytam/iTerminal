using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Helper;

/// <summary>Records the prompts it is asked and answers with a fixed choice.</summary>
internal sealed class FakeConflictPromptService(FileConflictChoice choice = FileConflictChoice.Skip, bool applyToAll = false) : IConflictPromptService
{
    public int PromptCount { get; private set; }

    public Task<(FileConflictChoice Choice, bool ApplyToAll)> ChooseAsync(string title, string message, CancellationToken cancellationToken)
    {
        PromptCount++;
        return Task.FromResult((choice, applyToAll));
    }
}

internal static class ConflictResolvers
{
    /// <summary>The resolver a file task had before the change: ticked is overwrite, unticked is the error.</summary>
    public static FileConflictResolver Of(bool overwrite) =>
        new(overwrite ? FileConflictMode.OverwriteAll : FileConflictMode.Legacy, new FakeConflictPromptService());
}
