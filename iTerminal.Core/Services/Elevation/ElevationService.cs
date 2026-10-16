using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using iTerminal.Core.Services.Shell;

namespace iTerminal.Core.Services.Elevation;

public enum ElevationResult
{
    /// <summary>The elevated program was started (or, on Unix, will replace this process once it has shut down).</summary>
    Started,

    /// <summary>The user said no — the UAC prompt was dismissed, or sudo's password was not given.</summary>
    Cancelled,

    Failed,

    /// <summary>This system has no way to elevate from here.</summary>
    NotSupported,
}

public sealed record ElevationOutcome(ElevationResult Result, string? Message = null)
{
    public bool Started => Result == ElevationResult.Started;
}

/// <summary>Running with more rights: as a copy of this app, or one program at a time.</summary>
public interface IElevationService
{
    /// <summary>Whether this process already has them: an Administrator token on Windows, root elsewhere.</summary>
    bool IsElevated { get; }

    /// <summary>Whether there is a way to ask for them (UAC on Windows, <c>sudo</c> elsewhere).</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Starts this app again with elevated rights, in the same terminal where the platform allows.
    /// On <see cref="ElevationResult.Started"/> the caller quits: on Windows the new copy waits for this
    /// one to exit before it opens the data folder, and on Unix this process is replaced once it has
    /// shut down (<see cref="PendingRelaunch"/>), so the two never run side by side.
    /// </summary>
    Task<ElevationOutcome> RestartElevatedAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="filePath"/> is something <see cref="RunElevatedAsync"/> can start.</summary>
    bool CanRunElevated(string filePath);

    /// <summary>Runs one program with elevated rights — Explorer's "Run as administrator", or <c>sudo</c> on the file.</summary>
    Task<ElevationOutcome> RunElevatedAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// What must happen after the app has shut down: on Unix, replacing the process with
/// <c>sudo iTerminal</c>. It cannot be done sooner — the terminal has to be given back first, and the
/// settings and the database written — which is why a request is recorded here and carried out by
/// <c>Program.cs</c> once the host has stopped.
/// </summary>
public static class PendingRelaunch
{
    private static string[]? _command;

    public static bool IsPending => _command is not null;

    public static void Request(IEnumerable<string> command) => _command = [.. command];

    public static void Cancel() => _command = null;

    /// <summary>Replaces this process with the requested command. Returns only if that failed; false when nothing was requested.</summary>
    public static bool TryExecute()
    {
        var command = _command;
        _command = null;
        if (command is null || command.Length == 0 || OperatingSystem.IsWindows())
        {
            return false;
        }

        // execvp wants a null-terminated argv.
        var argv = new string?[command.Length + 1];
        Array.Copy(command, argv, command.Length);
        execvp(command[0], argv);
        return true;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int execvp(string file, string?[] argv);
}

/// <summary>
/// Windows: the <c>runas</c> verb, which is what raises the UAC prompt and gives the new process the
/// full token. Unix: <c>sudo</c>, with its password prompt shown on the real terminal (the shell
/// service lends it), then the app replaces itself with <c>sudo iTerminal</c>.
///
/// <para>A restarted app starts with <c>--wait-for-parent &lt;pid&gt;</c>, and waits for that process to
/// end before it opens the data folder — the settings are written back whole on exit, and two copies
/// writing them is how one's changes would be lost.</para>
/// </summary>
public sealed class ElevationService(IInteractiveShellService shellService) : IElevationService
{
    public const string WaitForParentArgument = "--wait-for-parent";

    private const int ErrorCancelled = 1223;

    private static readonly string[] WindowsRunAsExtensions = [".exe", ".com", ".bat", ".cmd", ".msi", ".msc", ".lnk"];

    public bool IsElevated => Environment.IsPrivilegedProcess;

    public bool IsSupported => OperatingSystem.IsWindows() || FindOnPath("sudo") is not null;

    /// <summary>The program and arguments that start this app again, as the shell sees them.</summary>
    private static (string Program, List<string> Arguments) SelfCommand()
    {
        var program = Environment.ProcessPath ?? "iTerminal";
        var arguments = new List<string>();
        var commandLine = Environment.GetCommandLineArgs();
        if (string.Equals(Path.GetFileNameWithoutExtension(program), "dotnet", StringComparison.OrdinalIgnoreCase)
            && commandLine.Length > 0)
        {
            // Started as `dotnet iTerminal.dll`: the library is the first argument.
            arguments.Add(commandLine[0]);
        }

        arguments.AddRange(commandLine.Skip(1).Where(a => a != WaitForParentArgument));
        return (program, arguments);
    }

    private static string Quote(string argument) =>
        argument.Length > 0 && argument.All(c => !char.IsWhiteSpace(c) && c != '"')
            ? argument
            : "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    public Task<ElevationOutcome> RestartElevatedAsync(CancellationToken cancellationToken = default)
    {
        if (IsElevated)
        {
            return Task.FromResult(new ElevationOutcome(ElevationResult.Failed, "Already running with elevated rights."));
        }

        return OperatingSystem.IsWindows() ? RestartWindowsAsync(cancellationToken) : RestartUnixAsync(cancellationToken);
    }

    private static Task<ElevationOutcome> RestartWindowsAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var (program, arguments) = SelfCommand();
            arguments.Add(WaitForParentArgument);
            arguments.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try
            {
                using var process = Process.Start(new ProcessStartInfo(program)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = string.Join(' ', arguments.Select(Quote)),
                    WorkingDirectory = Environment.CurrentDirectory,
                });
                return process is null
                    ? new ElevationOutcome(ElevationResult.Failed, "Windows did not start the elevated copy.")
                    : new ElevationOutcome(ElevationResult.Started);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
            {
                return new ElevationOutcome(ElevationResult.Cancelled, "The administrator prompt was dismissed.");
            }
            catch (Win32Exception exception)
            {
                return new ElevationOutcome(ElevationResult.Failed, exception.Message);
            }
        }, cancellationToken);

    private async Task<ElevationOutcome> RestartUnixAsync(CancellationToken cancellationToken)
    {
        var sudo = FindOnPath("sudo");
        if (sudo is null)
        {
            return new ElevationOutcome(ElevationResult.NotSupported, "sudo is not installed.");
        }

        // Authenticate now, on the real terminal, so a wrong password costs a message and not the app.
        var outcome = await shellService.RunAsync(
            new ShellLaunch(sudo, ["-v"], null, "sudo") { Banner = "iTerminal restarts as root — sudo asks for your password:" },
            cancellationToken).ConfigureAwait(false);
        if (!outcome.Started)
        {
            return new ElevationOutcome(ElevationResult.Failed, outcome.Failure ?? "sudo did not start.");
        }

        if (outcome.ExitCode != 0)
        {
            return new ElevationOutcome(ElevationResult.Cancelled, "sudo did not accept the password.");
        }

        var (program, arguments) = SelfCommand();
        PendingRelaunch.Request([sudo, "--", program, .. arguments]);
        return new ElevationOutcome(ElevationResult.Started);
    }

    public bool CanRunElevated(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || IsElevated)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsRunAsExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return FindOnPath("sudo") is not null
                && (File.GetUnixFileMode(filePath) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public async Task<ElevationOutcome> RunElevatedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!CanRunElevated(filePath))
        {
            return new ElevationOutcome(ElevationResult.NotSupported, $"{Path.GetFileName(filePath)} cannot be run as administrator.");
        }

        var folder = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
        if (OperatingSystem.IsWindows())
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var process = Process.Start(new ProcessStartInfo(filePath)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        WorkingDirectory = folder,
                    });
                    return new ElevationOutcome(ElevationResult.Started);
                }
                catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
                {
                    return new ElevationOutcome(ElevationResult.Cancelled, "The administrator prompt was dismissed.");
                }
                catch (Win32Exception exception)
                {
                    return new ElevationOutcome(ElevationResult.Failed, exception.Message);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        var sudo = FindOnPath("sudo")!;
        var outcome = await shellService.RunAsync(
            new ShellLaunch(sudo, ["--", filePath], folder, "sudo " + Path.GetFileName(filePath)),
            cancellationToken).ConfigureAwait(false);
        return outcome.Started
            ? new ElevationOutcome(ElevationResult.Started)
            : new ElevationOutcome(ElevationResult.Failed, outcome.Failure ?? "sudo did not start.");
    }

    private static string? FindOnPath(string program)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
