using System.Diagnostics;
using System.Text;

namespace iTerminal.Core.Services.Processes;

/// <summary>
/// <see cref="IExternalProcessRunner"/> over <see cref="Process"/>. Both pipes are drained
/// concurrently — a tool that fills stderr while the caller waits on stdout would otherwise
/// deadlock — and a text run that outlives <see cref="Timeout"/> is killed with its process tree,
/// so a hung <c>docker exec</c> cannot hold a background task forever.
/// </summary>
public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    /// <summary>Ceiling for a text run. Listings and metadata queries; never a file transfer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<ProcessOutput> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        Process? process = null;
        try
        {
            process = Start(fileName, arguments, workingDirectory, redirectInput: false);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                cancellationToken.ThrowIfCancellationRequested();
                return new ProcessOutput(-1, string.Empty, $"{fileName} did not answer within {Timeout.TotalSeconds:0} seconds.");
            }

            return new ProcessOutput(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ProcessOutput(-1, string.Empty, DescribeStartFailure(fileName, exception));
        }
        finally
        {
            process?.Dispose();
        }
    }

    public async Task<ProcessOutput> RunToStreamAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Stream destination,
        string? workingDirectory = null,
        Func<Stream, Task>? writeStandardInput = null,
        CancellationToken cancellationToken = default)
    {
        Process? process = null;
        try
        {
            process = Start(fileName, arguments, workingDirectory, redirectInput: writeStandardInput is not null);
            using var registration = cancellationToken.Register(static state => Kill((Process)state!), process);

            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(destination, cancellationToken);

            if (writeStandardInput is not null)
            {
                try
                {
                    await writeStandardInput(process.StandardInput.BaseStream);
                }
                finally
                {
                    process.StandardInput.Close();
                }
            }

            await stdoutTask;
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessOutput(process.ExitCode, string.Empty, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (process is null)
        {
            return new ProcessOutput(-1, string.Empty, DescribeStartFailure(fileName, exception));
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static Process Start(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, bool redirectInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        // No TTY exists to answer a credential or passphrase prompt; fail instead of hanging.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }

    private static string DescribeStartFailure(string fileName, Exception exception) =>
        exception is System.ComponentModel.Win32Exception
            ? $"{fileName} is not installed or not on PATH."
            : exception.Message;
}
