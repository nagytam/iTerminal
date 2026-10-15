using System.Diagnostics;

namespace iTerminal.Core.Services.PackageManager;

/// <summary>Shared streaming-process runner for <see cref="IPackageManagerService"/> implementations.</summary>
internal static class PackageManagerProcess
{
    public readonly record struct ProcessResult(bool Success, string StandardOutput, string StandardError);

    /// <summary>Picks the first non-blank of stderr/stdout as an error message, or the fallback.</summary>
    public static string FirstNonEmpty(string first, string second, string fallback) =>
        !string.IsNullOrWhiteSpace(first) ? first.Trim() : !string.IsNullOrWhiteSpace(second) ? second.Trim() : fallback;

    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> and reads the whole of
    /// stdout/stderr, for a short-lived query command (e.g. list/search) whose full output must be
    /// parsed at once. Enforces <paramref name="timeout"/> since some package manager sources hang
    /// rather than fail when unreachable.
    /// </summary>
    public static async Task<ProcessResult> RunCaptureAsync(string fileName, TimeSpan timeout, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // Drain both pipes concurrently so a full stderr buffer can't deadlock a full stdout.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new ProcessResult(false, string.Empty, $"{fileName} command timed out");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new ProcessResult(process.ExitCode == 0, stdout, stderr);
        }
        catch (Exception exception)
        {
            return new ProcessResult(false, string.Empty, exception.Message);
        }
    }

    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/>, streaming each line of
    /// stdout/stderr to <paramref name="onOutputLine"/> as it is produced, and kills the whole
    /// process tree on cancellation so a long install/uninstall can be stopped from the UI.
    /// </summary>
    public static async Task<int> RunStreamingAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };

        try
        {
            if (!process.Start())
            {
                onOutputLine($"Failed to start {fileName}.");
                return -1;
            }
        }
        catch (Exception exception)
        {
            onOutputLine($"Failed to start {fileName}: {exception.Message}");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            onOutputLine("(cancelled)");
            return -1;
        }
    }
}
