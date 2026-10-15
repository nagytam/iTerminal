using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model;

namespace iTerminal.Core.Commands;

public class TerminalExecuteCommand(TerminalModel terminalModel) : Command
{
    public IContentModelService? ContentModelService { get; internal set; }
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        if (terminalModel is not null 
            && ContentModelService is not null)
        {
            var model = ContentModelService.GetLastContentModel();
            if (model is not null && model.Url is not null)
            {
                terminalModel.Command.WorkingDirectoryUrl = model.Url;
                terminalModel.StartCommand();
            }
        }
        return Task.CompletedTask;
    }
}
