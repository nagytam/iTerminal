using System.Collections.Concurrent;
using iConsole.Data;

namespace iConsole.Inputs;

/// <summary>
/// Queues produced by a terminal input publisher (Win32 console records, Linux TTY bytes, …)
/// and drained by <see cref="Jobs.TerminalInputConsumerJob"/>.
/// </summary>
public interface ITerminalInputSource
{
    ConcurrentQueue<MouseEvent> MouseQueue { get; }
    ConcurrentQueue<KeyEvent> KeyQueue { get; }
    SemaphoreSlim InputAvailableSignal { get; }
}
