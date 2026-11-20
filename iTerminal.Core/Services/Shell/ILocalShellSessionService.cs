namespace iTerminal.Core.Services.Shell;

/// <summary>
/// The long-lived shell the command log runs its commands in: one <see cref="LocalShellSession"/> at a time,
/// for a local folder, of the kind the user selected.
/// </summary>
public interface ILocalShellSessionService : IDisposable
{
    /// <summary>The kinds that can run here: sh and bash when they are on PATH; none on Windows in this version.</summary>
    IReadOnlyList<ShellKind> Available();

    /// <summary>Whether a session can run here at all. False means commands take the one-shot path.</summary>
    bool IsAvailable { get; }

    /// <summary>The kind a session starts as: the setting when it is available, else <c>$SHELL</c>'s kind, else sh.</summary>
    ShellKind Selected { get; }

    /// <summary>True while a command runs in the session. Enter-runs and typed commands both check it.</summary>
    bool IsBusy { get; }

    /// <summary>The names of the sandboxes that can run here; <c>none</c> is always one of them.</summary>
    IReadOnlyList<string> AvailableSandboxes();

    /// <summary>The sandbox a session starts through: the saved choice when it is available, else <c>none</c>.</summary>
    string SelectedSandbox { get; }

    /// <summary>
    /// Selects the sandbox <paramref name="name"/> and saves it as the default. Ends the current session, so its
    /// variables and folder are gone. Refused (false) while a command runs or when the sandbox is not available.
    /// </summary>
    bool SwitchSandbox(string name);

    /// <summary>
    /// Selects <paramref name="kind"/> and saves it as the default. Ends the current session, so its variables and
    /// folder are gone. Refused (false) while a command runs or when the shell is not available.
    /// </summary>
    bool Switch(ShellKind kind);

    /// <summary>
    /// Runs <paramref name="command"/> in the session. A session starts in <paramref name="folder"/>; an existing one
    /// keeps its folder unless the panel has moved since its last command, in which case it changes to it first.
    /// Throws when <see cref="IsAvailable"/> is false or a command is already running.
    /// </summary>
    Task<ShellCommandResult> RunAsync(string folder, string command, CancellationToken cancellationToken = default);

    /// <summary>Ends the running session (Esc). Non-blocking.</summary>
    void Interrupt();
}
