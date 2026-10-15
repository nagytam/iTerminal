namespace iConsole.Diagnostics;

/// <summary>
/// Observes every <see cref="Commands.Command.RunAsync"/> call — the single funnel all command
/// execution passes through, regardless of whether it was triggered by a key event, a hotkey,
/// or invoked directly. Attach an <see cref="InMemoryCommandLogger"/>-like implementation (see
/// iConsole.Test.Core) to agentic/integration tests to record what actions the app took.
/// </summary>
public interface ICommandLogger
{
    void Log(CommandExecutionRecord record);
}
