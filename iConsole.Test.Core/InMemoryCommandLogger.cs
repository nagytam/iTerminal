using iConsole.Diagnostics;

namespace iConsole.Test.Core;

/// <summary>
/// ICommandLogger that records every command execution in memory so agentic/integration tests
/// can assert on which commands ran, in what order, with what result — after driving the app
/// via key events (e.g. <c>panel.HandleKeyEventAsync(...)</c>).
/// Attach it to a control tree with <c>control.SetCommandLogger(logger)</c>.
/// </summary>
public sealed class InMemoryCommandLogger : ICommandLogger
{
    private readonly List<CommandExecutionRecord> _records = [];

    public IReadOnlyList<CommandExecutionRecord> Records
    {
        get { lock (_records) { return _records.ToArray(); } }
    }

    public CommandExecutionRecord? Last => Records.LastOrDefault();

    public void Log(CommandExecutionRecord record)
    {
        lock (_records) { _records.Add(record); }
    }

    public void Clear()
    {
        lock (_records) { _records.Clear(); }
    }
}
