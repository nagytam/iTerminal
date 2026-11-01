using iConsole.Prompts;

namespace iConsole.Services;

/// <summary>
/// Asks the person a question in a dialog and waits for the answer. Prompts are shown one at a time, so concurrent
/// callers queue. Each method returns null (or false for <see cref="ConfirmAsync"/>'s nullable form) when the prompt is
/// cancelled with Esc or by <paramref name="cancellationToken"/>.
/// </summary>
public interface IPromptService
{
    /// <summary>Asks for a value. Null when cancelled.</summary>
    Task<T?> AskAsync<T>(TextPrompt<T> prompt, CancellationToken cancellationToken = default);

    /// <summary>Asks the person to choose one of the choices. Null when cancelled.</summary>
    Task<T?> SelectAsync<T>(SelectionPrompt<T> prompt, CancellationToken cancellationToken = default);

    /// <summary>Asks the person to choose any number of the choices. Null when cancelled.</summary>
    Task<IReadOnlyList<T>?> MultiSelectAsync<T>(MultiSelectionPrompt<T> prompt, CancellationToken cancellationToken = default);

    /// <summary>Asks a yes-or-no question. Null when cancelled.</summary>
    Task<bool?> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken = default);
}
