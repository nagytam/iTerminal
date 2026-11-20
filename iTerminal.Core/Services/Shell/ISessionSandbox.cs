namespace iTerminal.Core.Services.Shell;

/// <summary>The program a session starts: the shell itself, or a sandbox wrapping it.</summary>
public sealed record SessionLaunch(string Program, IReadOnlyList<string> Arguments);

/// <summary>
/// How the command log's shell session is confined. Each implementation rewrites the shell's launch command:
/// <see cref="NoSandbox"/> leaves it alone, <see cref="BubblewrapSandbox"/> runs it in a namespace sandbox. The
/// one chosen is saved by <see cref="SessionSandboxes.SettingKey"/>, and <see cref="ILocalShellSessionService"/>
/// starts every session through it.
///
/// <para>A sandbox that cannot start its program does not fall back to running the shell unconfined: the
/// command's error output says what failed.</para>
/// </summary>
public interface ISessionSandbox
{
    /// <summary>The name the setting and the <c>sandbox</c> line use. Unique across implementations.</summary>
    string Name { get; }

    /// <summary>Whether this sandbox can be used on this machine (its tool is installed, the OS supports it).</summary>
    bool IsAvailable { get; }

    /// <summary>The launch command that starts <paramref name="shell"/> confined to <paramref name="folder"/>.</summary>
    SessionLaunch Wrap(SessionLaunch shell, string folder);

    /// <summary>
    /// Starts <paramref name="shell"/> itself, for a sandbox that creates the process rather than rewriting its
    /// command (<see cref="ProcessContainerSandbox"/>). Null means the session starts the process from
    /// <see cref="Wrap"/>. A sandbox that returns a process must not also rely on <see cref="Wrap"/>.
    /// </summary>
    IShellProcess? Start(SessionLaunch shell, string folder) => null;
}
