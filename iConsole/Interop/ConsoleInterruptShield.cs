using System.Runtime.InteropServices;

namespace iConsole.Interop;

/// <summary>
/// Keeps Ctrl+C, Ctrl+\ and Ctrl+Z away from this process while a child it lent the terminal to is
/// running.
///
/// <para>The keys reach every process attached to the console (Windows) or in the terminal's
/// foreground process group (Linux) — the app as well as the shell it started. The generic host's
/// console lifetime stops the app on Ctrl+C, so without this an interrupted <c>ping</c> in the shell
/// quit iTerminal from under it, and on Linux Ctrl+Z suspended it. A handler cannot prevent that:
/// every registered handler runs, the host's among them, so the signal is ignored at the OS level.</para>
///
/// <para>Raise it only <em>after</em> the child has started: an ignored Ctrl+C is inherited on both
/// platforms, and a shell that cannot be interrupted is worse than the problem.</para>
/// </summary>
internal static class ConsoleInterruptShield
{
    public static IDisposable Raise()
    {
        if (OperatingSystem.IsWindows())
        {
            return SetConsoleCtrlHandler(IntPtr.Zero, true) ? new Restore(() => SetConsoleCtrlHandler(IntPtr.Zero, false)) : Restore.Nothing;
        }

        if (OperatingSystem.IsLinux())
        {
            List<(int Signal, byte[] Previous)> saved = [];
            foreach (var signal in LinuxSignals)
            {
                var previous = new byte[SigactionSize];
                if (sigaction(signal, IgnoreAction(), previous) == 0)
                {
                    saved.Add((signal, previous));
                }
            }

            return new Restore(() =>
            {
                foreach (var (signal, previous) in saved)
                {
                    _ = sigaction(signal, previous, null);
                }
            });
        }

        return Restore.Nothing;
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public static readonly Restore Nothing = new(() => { });

        private Action? _undo = undo;

        public void Dispose() => Interlocked.Exchange(ref _undo, null)?.Invoke();
    }

    #region Windows

    /// <summary>With a null handler, TRUE makes this process ignore Ctrl+C, and FALSE undoes it.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);

    #endregion

    #region Linux

    private const int SIGINT = 2;
    private const int SIGQUIT = 3;
    private const int SIGTSTP = 20;
    private static readonly int[] LinuxSignals = [SIGINT, SIGQUIT, SIGTSTP];

    /// <summary>
    /// Room for glibc's <c>struct sigaction</c> (152 bytes on x64 and arm64, handler first). It is
    /// treated as opaque: what is saved is handed back as it was, which is the only way to put the
    /// runtime's own <c>SA_SIGINFO</c> handler back — <c>signal()</c> would reinstall it with the
    /// wrong calling convention.
    /// </summary>
    private const int SigactionSize = 256;

    /// <summary>A <c>struct sigaction</c> whose handler is <c>SIG_IGN</c> (1), with an empty mask and no flags.</summary>
    private static byte[] IgnoreAction()
    {
        var action = new byte[SigactionSize];
        MemoryMarshal.Write(action.AsSpan(), (nint)1);
        return action;
    }

    [DllImport("libc", EntryPoint = "sigaction", SetLastError = true)]
    private static extern int sigaction(int signal, byte[]? action, byte[]? previous);

    #endregion
}
