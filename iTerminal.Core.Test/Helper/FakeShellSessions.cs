using iTerminal.Core.Services.Shell;

namespace iTerminal.Core.Test.Helper;

/// <summary>
/// A command-log shell session that starts no process: it records each command and answers with <see cref="Answer"/>
/// (sh and bash both offered by default). Busy and availability are settable, so the routing and the "shell" line can
/// be tested without a shell on the machine.
/// </summary>
public sealed class FakeShellSessions : ILocalShellSessionService
{
    private readonly Lock _gate = new();
    private readonly List<(string Folder, string Command)> _calls = [];

    public bool IsAvailable { get; set; } = true;

    public bool IsBusy { get; set; }

    public ShellKind Selected { get; private set; } = ShellKind.Sh;

    /// <summary>What a command answers. Defaults to a clean result that keeps the folder it was given.</summary>
    public Func<string, string, ShellCommandResult> Answer { get; set; } = (folder, command) =>
        new ShellCommandResult("fake: " + command, string.Empty, 0, folder, ShellRunState.Completed);

    public int InterruptCount { get; private set; }

    /// <summary>The sandbox names offered. Defaults to none and bubblewrap; a switch is remembered.</summary>
    public List<string> Sandboxes { get; set; } = ["none", "bubblewrap"];

    public string SelectedSandbox { get; private set; } = "none";

    public IReadOnlyList<string> AvailableSandboxes() => Sandboxes;

    public bool SwitchSandbox(string name)
    {
        if (IsBusy || !Sandboxes.Contains(name))
        {
            return false;
        }

        SelectedSandbox = name;
        return true;
    }

    public IReadOnlyList<(string Folder, string Command)> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    public IReadOnlyList<ShellKind> Available() => ShellKinds.All;

    public bool Switch(ShellKind kind)
    {
        if (IsBusy)
        {
            return false;
        }

        Selected = kind;
        return true;
    }

    public Task<ShellCommandResult> RunAsync(string folder, string command, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _calls.Add((folder, command));
        }

        return Task.FromResult(Answer(folder, command));
    }

    public void Interrupt() => InterruptCount++;

    public void Dispose()
    {
    }
}
