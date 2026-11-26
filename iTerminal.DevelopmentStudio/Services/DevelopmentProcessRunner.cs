using System.Diagnostics;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>
/// Runs commands with redirected output. Cancelling kills the whole process tree, so a build node or a test host left
/// behind by a cancelled run does not keep the output open.
/// </summary>
public sealed class DevelopmentProcessRunner : IDevelopmentProcessRunner
{
    /// <inheritdoc />
    public async Task<int> RunAsync(ProcessCommand command, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = string.IsNullOrEmpty(command.WorkingDirectory) ? Environment.CurrentDirectory : command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) => exited.TrySetResult(process.ExitCode);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };

        try
        {
            if (!process.Start())
            {
                onOutputLine($"Could not start {command.FileName}.");
                return -1;
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            onOutputLine($"Could not start {command.FileName}: {exception.Message}");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            // Cancel before killing: killing raises the exit event at once, which would read as a finished run.
            exited.TrySetCanceled(cancellationToken);
            try { process.Kill(entireProcessTree: true); } catch {
                // Best effort: a process that already exited has nothing to kill.
            }
        });

        try
        {
            int exitCode = await exited.Task.ConfigureAwait(false);
            // The exit can come before the last output lines; give the readers a moment to drain.
            await Task.WhenAny(Task.Run(() => process.WaitForExit(), CancellationToken.None), Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            onOutputLine("(cancelled)");
            return -1;
        }
    }
}
