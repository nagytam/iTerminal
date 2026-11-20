using iConsole.Services;

namespace iTerminal.Core.Services.Taskbar;

/// <summary>
/// The taskbar's progress indicator as the terminal's own OSC 9;4 sequence (shown by Windows Terminal in the tab or the
/// taskbar), for <see cref="TaskbarState"/> to <see cref="TerminalProgressState"/>.
/// </summary>
public sealed class TerminalTaskbarProgress(ITerminalProgressService progress) : ITaskbarProgress
{
    public void Update(TaskbarState state, long percentage)
    {
        progress.Update(Map(state), percentage);
    }

    /// <summary>The terminal's state for an application state. Public so the mapping can be tested on its own.</summary>
    public static TerminalProgressState Map(TaskbarState state) => state switch
    {
        TaskbarState.Indeterminate => TerminalProgressState.Indeterminate,
        TaskbarState.Normal => TerminalProgressState.Normal,
        TaskbarState.Paused => TerminalProgressState.Paused,
        _ => TerminalProgressState.Remove,
    };
}
