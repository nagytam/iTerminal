using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Commands;

public class ConnectRemoteShellCommand(
        HostValidationModel hostValidator,
        IContentModelService contentModelService
    ) : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        var canExecute = base.CanExecute();
        if (Control?.Parent is FolderSelectorRemoteShellTabPage tabPage)
        {
            canExecute = canExecute && hostValidator.EnsureCurrent();
        }
        return canExecute;
    }

    public override async Task ExecuteAsync(object? arg)
    {
        var sshTabPage = Control?.Parent as FolderSelectorRemoteShellTabPage;
        if (sshTabPage is not null)
        {
            var host = sshTabPage.Host;
            if (!host.Contains(":/") && !host.EndsWith("/"))
            {
                host += ":/";
            }
            if (!host.StartsWith(Container.SSH))
            {
                host = Container.SSH + host;
            }
            await contentModelService.SetUrlAsync(host);
            (Control?.Parent as FolderSelectorRemoteShellTabPage)?.TextBoxHost.AutoCompletion?.CompletionModel.InsertCompletion(host);
            CloseContainingDialog();
        }
    }
}
