namespace iTerminal.Core.Services.Shell;

/// <summary>The sandboxes' names and the setting that saves the choice.</summary>
public static class SessionSandboxes
{
    /// <summary>The settings key naming the command-log sandbox. Not a reserved <c>__</c> key.</summary>
    public const string SettingKey = "TerminalSandbox";

    /// <summary>The name of the sandbox that changes nothing.</summary>
    public const string NoneName = "none";
}

/// <summary>The default: the shell starts as it always has, with no confinement.</summary>
public sealed class NoSandbox : ISessionSandbox
{
    public string Name => SessionSandboxes.NoneName;

    public bool IsAvailable => true;

    public SessionLaunch Wrap(SessionLaunch shell, string folder) => shell;
}

/// <summary>
/// Runs the shell under bubblewrap (<c>bwrap</c>, Linux): the whole filesystem read-only, the session's folder
/// writable, a private <c>/tmp</c>, and the process and its children dying with the app. The session can still
/// <c>cd</c> anywhere it can read; it can write only in the folder it started in.
/// </summary>
public class BubblewrapSandbox : ISessionSandbox
{
    private const string Program = "bwrap";

    public virtual string Name => "bubblewrap";

    /// <summary>Whether the shell keeps the machine's network. The offline variant turns it off.</summary>
    protected virtual bool ShareNetwork => true;

    public bool IsAvailable => OperatingSystem.IsLinux() && ShellLaunch.FindOnPath(Program) is not null;

    public SessionLaunch Wrap(SessionLaunch shell, string folder)
    {
        // Mounts apply in order: the private /tmp first, so a session folder under /tmp is bound over it.
        var arguments = new List<string>
        {
            "--ro-bind", "/", "/",
            "--dev", "/dev",
            "--proc", "/proc",
            #pragma warning disable S5443 // A system temporary folder that the app only reads or writes its own files in; not a security boundary here.
            "--tmpfs", "/tmp",
            #pragma warning restore S5443
            "--die-with-parent",
        };

        if (!ShareNetwork)
        {
            arguments.Add("--unshare-net");
        }

        if (Directory.Exists(folder))
        {
            arguments.AddRange(["--bind", folder, folder, "--chdir", folder]);
        }

        arguments.Add(shell.Program);
        arguments.AddRange(shell.Arguments);
        return new SessionLaunch(Program, arguments);
    }
}

/// <summary>Bubblewrap with the network unshared: the session has no route out.</summary>
public sealed class BubblewrapOfflineSandbox : BubblewrapSandbox
{
    public override string Name => "bubblewrap-offline";

    protected override bool ShareNetwork => false;
}
