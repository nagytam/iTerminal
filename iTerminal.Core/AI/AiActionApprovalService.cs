using iConsole.Desktops;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Core.AI;

public sealed class AiActionApprovalService(IServiceProvider serviceProvider, IThemeService themeService) : IAiActionApprovalService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<bool> ConfirmAsync(AiActionApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Actions.Count == 0)
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        AiActionApprovalDialog? dialog = null;
        try
        {
            IDesktop desktop = serviceProvider.GetRequiredService<IDesktop>();
            var screen = desktop.Screens.FirstOrDefault()
                ?? throw new InvalidOperationException("No screen is available to approve the AI action.");
            dialog = new AiActionApprovalDialog(request, themeService);
            await screen.ShowDialogAsync(dialog);
            return await dialog.Result.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            dialog?.Close();
            throw;
        }
        finally
        {
            if (dialog is not null && !dialog.Result.IsCompleted)
            {
                dialog.Close();
            }

            _gate.Release();
        }
    }
}
