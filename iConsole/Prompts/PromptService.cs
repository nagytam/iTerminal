using iConsole.Desktops;
using iConsole.Dialogs;
using iConsole.Services;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Prompts;

/// <summary>
/// Shows the prompt dialogs on the first screen and waits for each answer. Like <see cref="ErrorPromptService"/>, it resolves
/// <see cref="IDesktop"/> when a prompt is shown rather than in its constructor, because the desktop's own graph depends on
/// the services that use prompts.
/// </summary>
public sealed class PromptService(
    IServiceProvider serviceProvider,
    IBeepService beepService,
    IDateTimeProviderService dateTimeProviderService,
    IThemeService themeService) : IPromptService
{
    // One prompt on screen at a time: a second prompt waits for the first to be answered.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<T?> AskAsync<T>(TextPrompt<T> prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return ShowAsync(new TextPromptDialog<T>(prompt, beepService, dateTimeProviderService, themeService), cancellationToken);
    }

    public Task<T?> SelectAsync<T>(SelectionPrompt<T> prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return ShowAsync(new SelectionPromptDialog<T>(prompt, themeService), cancellationToken);
    }

    public Task<IReadOnlyList<T>?> MultiSelectAsync<T>(MultiSelectionPrompt<T> prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return ShowAsync(new MultiSelectionPromptDialog<T>(prompt, themeService), cancellationToken);
    }

    public Task<bool?> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return ShowAsync(new ConfirmationPromptDialog(prompt, themeService), cancellationToken);
    }

    private async Task<TResult?> ShowAsync<TResult>(PromptDialog<TResult> dialog, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var desktop = serviceProvider.GetRequiredService<IDesktop>();
            var root = desktop.Screens.First();
            await root.ShowDialogAsync(dialog);
            try
            {
                return await dialog.Result.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The caller has gone; take the prompt down rather than leave it grabbing the keys.
                dialog.Close();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
