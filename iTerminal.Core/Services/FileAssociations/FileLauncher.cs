using iTerminal.Core.Services.Shell;
using System.ComponentModel;
using System.Diagnostics;

namespace iTerminal.Core.Services.FileAssociations;

/// <summary>Whether a program was started, and why not when it was not.</summary>
public sealed record LaunchOutcome(bool Started, string? Failure = null)
{
    public static LaunchOutcome Success { get; } = new(true);

    public static LaunchOutcome Failed(string failure) => new(false, failure);
}

/// <summary>
/// Starts programs on files. Async by contract: starting a process, and above all a terminal
/// program's whole session, never runs on a key handler.
/// </summary>
public interface IFileLauncher
{
    /// <summary>
    /// Opens <paramref name="filePath"/> with <paramref name="association"/>. Completes once a
    /// windowed program has started; for a terminal program, once it has exited and the app has
    /// the terminal back.
    /// </summary>
    Task<LaunchOutcome> LaunchAsync(FileAssociation association, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Opens <paramref name="filePath"/> with whatever the operating system associates with it.</summary>
    Task<LaunchOutcome> OpenWithSystemDefaultAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Opens a web address in the system's browser.</summary>
    Task<LaunchOutcome> OpenUrlAsync(string url, CancellationToken cancellationToken = default);
}

public class FileLauncher(IInteractiveShellService shellService) : IFileLauncher
{
    public async Task<LaunchOutcome> LaunchAsync(FileAssociation association, string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        if (FileAssociation.BuildCommandLine(association.Command, filePath) is not { } commandLine)
        {
            return LaunchOutcome.Failed($"{association.Name} has no command to run.");
        }

        if (ResolveProgram(commandLine.FileName) is not { } program)
        {
            return LaunchOutcome.Failed($"{commandLine.FileName} was not found on the PATH.");
        }

        var folder = Path.GetDirectoryName(filePath);
        if (association.InTerminal)
        {
            // The whole terminal, as Ctrl+O lends it to a shell: an editor needs the real keys and
            // the real screen, and gives them back when it exits.
            var outcome = await shellService.RunAsync(
                new ShellLaunch(program, commandLine.Arguments, folder, Path.GetFileName(filePath)) { Banner = string.Empty },
                cancellationToken);
            return outcome.Started ? LaunchOutcome.Success : LaunchOutcome.Failed(outcome.Failure ?? $"{association.Name} did not start.");
        }

        return await Task.Run(() => StartDetached(program, commandLine.Arguments, folder), cancellationToken);
    }

    public Task<LaunchOutcome> OpenWithSystemDefaultAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var process = Process.Start(new ProcessStartInfo(filePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(filePath) ?? string.Empty,
                    });
                    return LaunchOutcome.Success;
                }

                var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
                return ResolveProgram(opener) is { } program
                    ? StartDetached(program, [filePath], Path.GetDirectoryName(filePath))
                    : LaunchOutcome.Failed($"{opener} is not installed, so there is no system default to ask.");
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
            {
                return LaunchOutcome.Failed($"Nothing is set up to open {Path.GetFileName(filePath)}: {exception.Message}");
            }
        }, cancellationToken);

    public Task<LaunchOutcome> OpenUrlAsync(string url, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            // Only web addresses: whatever else was asked for, a shell would happily start as a program.
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? address) || address.Scheme is not ("http" or "https"))
            {
                return LaunchOutcome.Failed($"{url} is not a web address.");
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var process = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
                    return LaunchOutcome.Success;
                }

                var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
                return ResolveProgram(opener) is { } program
                    ? StartDetached(program, [address.AbsoluteUri], null)
                    : LaunchOutcome.Failed($"{opener} is not installed, so there is no browser to ask.");
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
            {
                return LaunchOutcome.Failed($"The browser could not be started: {exception.Message}");
            }
        }, cancellationToken);

    /// <summary>
    /// Starts a program that has a window of its own and lets it go. It gets no console and its
    /// output is thrown away: inheriting the app's, a tool that prints a line on start-up — a good
    /// many GUI programs do — would print it across the panels.
    /// </summary>
    private static LaunchOutcome StartDetached(string program, IReadOnlyList<string> arguments, string? folder)
    {
        var startInfo = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            startInfo.WorkingDirectory = folder;
        }

        try
        {
            var process = Process.Start(startInfo);
            if (process is null)
            {
                return LaunchOutcome.Failed($"{Path.GetFileName(program)} did not start.");
            }

            // Closed, so a program that asks a question gets an end of input instead of waiting
            // for ever; drained, so one that talks a lot does not block on a full pipe.
            process.StandardInput.Close();
            process.OutputDataReceived += static (_, _) => { };
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => process.Dispose();
            return LaunchOutcome.Success;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return LaunchOutcome.Failed($"Cannot start {Path.GetFileName(program)}: {exception.Message}");
        }
    }

    /// <summary>
    /// The file <paramref name="name"/> stands for: itself when it is a path, otherwise the first
    /// match on the PATH — trying the <c>PATHEXT</c> extensions on Windows, where <c>code</c> is
    /// <c>code.cmd</c> and a process cannot be started by a name without its extension.
    /// </summary>
    internal static string? ResolveProgram(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var candidates = new List<string> { name };
        if (OperatingSystem.IsWindows() && !Path.HasExtension(name))
        {
            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            candidates = [.. extensions.Select(extension => name + extension.ToLowerInvariant())];
        }

        if (name.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(name))
        {
            return candidates.FirstOrDefault(File.Exists);
        }

        foreach (var candidate in candidates)
        {
            if (ShellLaunch.FindOnPath(candidate) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
