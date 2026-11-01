using iConsole.Desktops;
using iConsole.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Services;

// IDesktop is resolved lazily, for the same reason as in ErrorPromptService: the desktop's UI graph
// depends on the services that ask this one, so taking it in the constructor would deadlock start-up.
public class ConflictPromptService(IServiceProvider serviceProvider, IThemeService themeService) : IConflictPromptService
{
    // Prompts are queued one at a time, so concurrent callers never stack dialogs.
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<(FileConflictChoice Choice, bool ApplyToAll)> ChooseAsync(string title, string message, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var desktop = serviceProvider.GetRequiredService<IDesktop>();
            var dialog = new ConflictPromptDialog(title, message, themeService);
            var root = desktop.Screens.First();
            await root.ShowDialogAsync(dialog);
            try
            {
                return await dialog.WaitForChoiceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The operation that asked has gone: take its prompt down, or it would keep the keyboard.
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
