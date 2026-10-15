using iConsole.Data;

namespace iConsole.Interfaces;

public interface ITerminalModel
{
    public ITerminalCommand Command { get; }
    public ConcurrentList<ITerminalCommand> History { get; set; }
}
