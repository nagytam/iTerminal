using iConsole.Services;

namespace iTerminal.Core.Test.Helper;

/// <summary>
/// Test double for <see cref="IErrorPromptService"/> that never shows real UI (which would hang a
/// headless test run) — it just records what it was asked and returns a fixed choice. Defaults to
/// "Continue" for tests where no errors are expected to occur at all.
/// </summary>
internal sealed class FakeErrorPromptService(bool shouldContinue = true) : IErrorPromptService
{
    public int PromptCount { get; private set; }
    public List<string> Messages { get; } = [];

    public Task<bool> ConfirmContinueAsync(string title, string message, CancellationToken cancellationToken)
    {
        PromptCount++;
        Messages.Add(message);
        return Task.FromResult(shouldContinue);
    }
}
