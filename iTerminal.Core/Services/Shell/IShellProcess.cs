using System.Diagnostics;

namespace iTerminal.Core.Services.Shell;

/// <summary>
/// The running shell as a session sees it: redirected stdio and its exit. <see cref="LocalShellSession"/> works
/// against this, so a sandbox that creates the process itself (<see cref="ProcessContainerSandbox"/>) can supply
/// the streams without going through <see cref="System.Diagnostics.Process"/>.
/// </summary>
public interface IShellProcess : IDisposable
{
    bool HasExited { get; }

    TextWriter StandardInput { get; }

    TextReader StandardOutput { get; }

    TextReader StandardError { get; }

    /// <summary>Kills the process and everything it started. Must not block.</summary>
    void Kill();

    /// <summary>Waits up to <paramref name="milliseconds"/> for the process to exit.</summary>
    void WaitForExit(int milliseconds);
}

/// <summary>The default: a shell started as an ordinary child process with redirected stdio.</summary>
internal sealed class ChildShellProcess(Process process) : IShellProcess
{
    public bool HasExited => process.HasExited;

    public TextWriter StandardInput => process.StandardInput;

    public TextReader StandardOutput => process.StandardOutput;

    public TextReader StandardError => process.StandardError;

    public void Kill() => process.Kill(entireProcessTree: true);

    public void WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);

    public void Dispose() => process.Dispose();
}
