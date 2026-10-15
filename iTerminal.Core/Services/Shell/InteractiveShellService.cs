using iConsole.Services;
using System.ComponentModel;

namespace iTerminal.Core.Services.Shell;

/// <summary>How a Ctrl+O session ended: the shell's exit code, or why it never started.</summary>
public sealed record ShellOutcome(int ExitCode, string? Failure)
{
    public bool Started => Failure is null;
}

/// <summary>
/// Hands the whole terminal to an interactive shell (Ctrl+O) and takes it back when the shell exits —
/// a real one, with its prompt, line editing, colours, full-screen programs and Ctrl+C, which the
/// app's own terminal panel (one command at a time, output captured) cannot be.
///
/// <para>The handover itself is <see cref="ConsoleOwnership"/>'s. This adds what a person needs
/// around it: a line saying where the shell runs and how to get back, and — when the program could
/// not start, or gave up within a moment with an error (no Docker, a stopped container, a refused
/// ssh login) — a pause on its message, which the repaint would otherwise wipe before it was read.</para>
/// </summary>
public sealed class InteractiveShellService(ConsoleOwnership consoleOwnership) : IInteractiveShellService
{
    /// <summary>A failing exit sooner than this is a shell that never got going, and its last words are shown until Enter.</summary>
    public static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(3);

    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public async Task<ShellOutcome> RunAsync(ShellLaunch launch, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            return new ShellOutcome(-1, "A shell is already running.");
        }

        try
        {
            return await consoleOwnership.LendAsync(() => Run(launch), cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private static ShellOutcome Run(ShellLaunch launch)
    {
        var banner = launch.Banner ?? $"iTerminal: shell in {launch.Location} — type exit to return to the panels.";
        if (banner.Length > 0)
        {
            Say(banner);
        }

        var started = DateTime.UtcNow;
        int exitCode;
        string? failure = null;
        try
        {
            exitCode = ConsoleOwnership.RunInherited(launch.ToStartInfo());
        }
        catch (Win32Exception exception)
        {
            // Not found, not executable: Process.Start's own words name neither the program nor the fix.
            exitCode = -1;
            failure = $"Cannot start {launch.FileName}: {exception.Message}";
        }

        if (failure is not null || (exitCode != 0 && DateTime.UtcNow - started < QuickExit))
        {
            Say(failure ?? $"{Path.GetFileName(launch.FileName)} exited with code {exitCode}.");
            Say("Press Enter to return to iTerminal.");
            try
            {
                Console.ReadLine();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                // No console to wait on (a test host): nothing to read the message on either.
            }
        }

        return new ShellOutcome(exitCode, failure);
    }

    private static void Say(string line)
    {
        try
        {
            Console.WriteLine(line);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>See <see cref="InteractiveShellService"/>.</summary>
public interface IInteractiveShellService
{
    /// <summary>Whether a shell has the terminal now.</summary>
    bool IsRunning { get; }

    /// <summary>Runs <paramref name="launch"/> on the whole terminal and completes when it has exited and the app has the terminal back.</summary>
    Task<ShellOutcome> RunAsync(ShellLaunch launch, CancellationToken cancellationToken = default);
}
