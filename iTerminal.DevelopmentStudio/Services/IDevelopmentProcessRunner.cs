using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>Runs a <see cref="ProcessCommand"/> and streams its output lines; the build, run, clean and test commands go through it.</summary>
public interface IDevelopmentProcessRunner
{
    /// <summary>
    /// Runs the command, passing each line of standard output and standard error to <paramref name="onOutputLine"/>
    /// as it arrives. Returns the exit code, or a negative value when the command could not start or was cancelled.
    /// </summary>
    Task<int> RunAsync(ProcessCommand command, Action<string> onOutputLine, CancellationToken cancellationToken = default);
}
