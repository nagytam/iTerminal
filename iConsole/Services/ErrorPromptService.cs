using iConsole.Desktops;
using iConsole.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Services;

// IDesktop is resolved lazily via the service provider rather than injected into the constructor.
// The desktop's UI graph transitively depends on IErrorPromptService (Desktop → screen → menu bar →
// Copy/Move/Delete dialogs → IErrorPromptService), so taking IDesktop as a constructor parameter
// forms a dependency cycle that deadlocks the container while it builds the Desktop singleton at
// startup (nothing ever renders). The desktop is only needed when a prompt is actually shown — long
// after startup, on a background pipeline thread — so resolving it on demand breaks the cycle.
public class ErrorPromptService(IServiceProvider serviceProvider, IThemeService themeService) : IErrorPromptService
{
    // Serializes prompts so concurrent workers hitting errors around the same time queue one
    // dialog at a time instead of stacking overlapping popups.
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task<bool> ConfirmContinueAsync(string title, string message, CancellationToken cancellationToken) =>
        ConfirmAsync(title, message, "Continue", "Cancel", cancellationToken);

    public async Task<bool> ConfirmAsync(string title, string message, string acceptLabel, string declineLabel, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var desktop = serviceProvider.GetRequiredService<IDesktop>();
            var dialog = new ErrorPromptDialog(title, message, themeService, acceptLabel, declineLabel);
            var root = desktop.Screens.First();
            await root.ShowDialogAsync(dialog);
            try
            {
                return await dialog.WaitForChoiceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The operation that asked has gone: its prompt is taken down, or it would stay on screen grabbing the keys.
                dialog.Abandon();
                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
