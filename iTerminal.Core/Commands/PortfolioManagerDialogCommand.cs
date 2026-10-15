using iConsole.Commands;
using iConsole.Data;
using iTerminal.Core.Dialogs.Portfolio;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

public class PortfolioManagerDialogCommand(
    PortfolioManagerDialog portfolioManagerDialog,
    PortfolioSetupWizardDialog portfolioSetupWizardDialog,
    ISettingsService settingsService) : Command
{
    private PortfolioManagerDialog _portfolioManagerDialog { get; init; } = portfolioManagerDialog;
    private PortfolioSetupWizardDialog _portfolioSetupWizardDialog { get; init; } = portfolioSetupWizardDialog;
    private ISettingsService _settingsService { get; init; } = settingsService;

    public override string Title => "Portfolio";
    public override string Description => "Open the Portfolio Manager (track holdings, live value, win/loss).";

    // Ctrl+P. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.P, false, false, true);

    public override async Task ExecuteAsync(object? arg)
    {
        if (Control is null)
        {
            return;
        }

        // First run: walk the user through quote source + default stocks before ever showing
        // the manager, then chain straight into it once the wizard completes.
        if (_settingsService.GetAsBool(PortfolioSetupWizardDialog.ConfiguredSettingKey, false) != true)
        {
            var control = Control;
            _portfolioSetupWizardDialog.OnCompleted = () => control.ShowDialogAsync(_portfolioManagerDialog);
            await control.ShowDialogAsync(_portfolioSetupWizardDialog);
            return;
        }

        await Control.ShowDialogAsync(_portfolioManagerDialog);
    }
}
