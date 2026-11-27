using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using iConsole.Localization;
using iTerminal.Core.Services.Dotnet;
using iTerminal.DevelopmentStudio.Model.NuGet;

namespace iTerminal.DevelopmentStudio.Services.NuGet;

/// <summary>
/// <see cref="INuGetPackageService"/> backed by <c>dotnet list</c>, <c>dotnet add</c> and <c>dotnet remove</c>. Each command runs
/// as a process on a background task; the listing reads the JSON of its standard output, so its error lines do not reach the
/// parser. Cancelling kills the whole process tree.
/// </summary>
public sealed class NuGetPackageService(IDotnetSolutionService solutions) : INuGetPackageService
{
    /// <summary>How long the output may still arrive after the process has exited (a build node can keep the pipe open).</summary>
    private static readonly TimeSpan OutputGracePeriod = TimeSpan.FromSeconds(2);

    public IReadOnlyList<string> ProjectsOf(string targetPath)
    {
        if (targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || targetPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return solutions.ParseSolution(targetPath).Projects.Select(project => project.AbsolutePath).ToList();
        }

        return [Path.GetFullPath(targetPath)];
    }

    public async Task<NuGetListing> ListAsync(string targetPath, string dotnetHost, CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? string.Empty;
        var installed = await RunAsync(dotnetHost, directory, NuGetCommands.ListArguments(targetPath, outdated: false), null, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (installed.Output.Length == 0)
        {
            return new NuGetListing([], [UiText.Get("NuGet.CouldNotRun", "Could not run dotnet to list the packages.")], Succeeded: false);
        }

        var outdated = await RunAsync(dotnetHost, directory, NuGetCommands.ListArguments(targetPath, outdated: true), null, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return NuGetListParser.Combine(installed.Output, outdated.Output.Length > 0 ? outdated.Output : null);
    }

    public async Task<int> AddAsync(string projectPath, string packageId, string? version, string dotnetHost, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        if (!NuGetCommands.IsPackageId(packageId) || (version is not null && !NuGetCommands.IsVersionText(version)))
        {
            onOutputLine(UiText.Get("NuGet.BadArgument", "That is not a package id or a version NuGet accepts."));
            return -1;
        }

        var (exitCode, _) = await RunAsync(dotnetHost, Path.GetDirectoryName(projectPath) ?? string.Empty, NuGetCommands.AddArguments(projectPath, packageId, version), onOutputLine, cancellationToken);
        return exitCode;
    }

    public async Task<int> RemoveAsync(string projectPath, string packageId, string dotnetHost, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        if (!NuGetCommands.IsPackageId(packageId))
        {
            onOutputLine(UiText.Get("NuGet.BadArgument", "That is not a package id or a version NuGet accepts."));
            return -1;
        }

        var (exitCode, _) = await RunAsync(dotnetHost, Path.GetDirectoryName(projectPath) ?? string.Empty, NuGetCommands.RemoveArguments(projectPath, packageId), onOutputLine, cancellationToken);
        return exitCode;
    }

    /// <summary>
    /// Runs <paramref name="host"/> with the arguments. Returns the exit code (negative when it could not start or was cancelled)
    /// and the lines it wrote to standard output. Standard error reaches only <paramref name="onOutputLine"/>.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunAsync(
        string host,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = host,
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var standardOutput = new List<string>();
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) => exited.TrySetResult(process.ExitCode);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (standardOutput) standardOutput.Add(e.Data);
            onOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) onOutputLine?.Invoke(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                onOutputLine?.Invoke(UiText.Get("NuGet.CouldNotRun", "Could not run dotnet to list the packages."));
                return (-1, string.Empty);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            onOutputLine?.Invoke($"{host}: {exception.Message}");
            return (-1, string.Empty);
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

        int exitCode;
        try
        {
            exitCode = await exited.Task;
        }
        catch (OperationCanceledException)
        {
            exitCode = -1;
        }

        // The exit can come before the last lines; give the readers a moment, not for a child that holds the pipe open.
        if (exitCode >= 0)
        {
            await Task.WhenAny(Task.Run(() => process.WaitForExit(), CancellationToken.None), Task.Delay(OutputGracePeriod, CancellationToken.None));
        }

        string output;
        lock (standardOutput) output = string.Join('\n', standardOutput);
        if (cancellationToken.IsCancellationRequested)
        {
            onOutputLine?.Invoke(UiText.Get("NuGet.Cancelled", "Cancelled."));
            return (-1, output);
        }

        return (exitCode, output);
    }
}
