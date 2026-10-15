using iConsole.Interop;
using System.Diagnostics;

namespace iConsole.Services;

/// <summary>
/// The application's claim on the terminal, and the one way to lend it to a child process — an
/// interactive shell, an editor, <c>ssh</c> — that needs the real thing: its keys, its screen, its
/// Ctrl+C.
///
/// <para>Two loops touch the terminal: the renderer writes to it and an input reader takes every
/// key from it. A child started while either runs would have its screen painted over and its
/// keystrokes stolen. So each of them brackets one pass of its loop with
/// <see cref="TryBeginUse"/>/<see cref="EndUse"/>, and skips the pass when it is refused. A lend
/// refuses new passes, waits for the ones under way, then asks every
/// <see cref="IConsoleHandoverParticipant"/> to undo its terminal state (raw mode, mouse reporting,
/// hidden cursor, colours) before the child starts — and to redo it, and the renderer to repaint
/// everything, once it has exited.</para>
///
/// <para>A pass is short (one frame, or one 50 ms wait for input), so a lend starts within a
/// frame. Lends are serialised: a second waits for the first to give the terminal back.</para>
/// </summary>
public sealed class ConsoleOwnership
{
    /// <summary>How long a lend waits for a pass under way before going ahead regardless.</summary>
    private static readonly TimeSpan UseDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private readonly List<IConsoleHandoverParticipant> _participants = [];
    private readonly SemaphoreSlim _lendGate = new(1, 1);
    private int _users;
    private bool _isLent;

    /// <summary>Whether a child process has the terminal now.</summary>
    public bool IsLent
    {
        get
        {
            lock (_sync)
            {
                return _isLent;
            }
        }
    }

    /// <summary>Raised on the lending thread once the terminal is back and every participant has redone its state.</summary>
    public event EventHandler? Reacquired;

    public void AddParticipant(IConsoleHandoverParticipant participant)
    {
        lock (_participants)
        {
            if (!_participants.Contains(participant))
            {
                _participants.Add(participant);
            }
        }
    }

    /// <summary>
    /// Starts one pass of a loop that touches the terminal; false while it is lent, and the pass
    /// must then be skipped. Every true must be matched by <see cref="EndUse"/>.
    /// </summary>
    public bool TryBeginUse()
    {
        lock (_sync)
        {
            if (_isLent)
            {
                return false;
            }

            _users++;
            return true;
        }
    }

    public void EndUse()
    {
        lock (_sync)
        {
            _users--;
            if (_users <= 0)
            {
                _users = 0;
                Monitor.PulseAll(_sync);
            }
        }
    }

    /// <summary>
    /// Lends the terminal to <paramref name="borrower"/>, which runs on a thread of its own for as
    /// long as it likes, and takes it back when it returns or throws.
    /// </summary>
    public async Task<T> LendAsync<T>(Func<T> borrower, CancellationToken cancellationToken = default)
    {
        await _lendGate.WaitAsync(cancellationToken);
        try
        {
            // A dedicated thread: an interactive session holds it for minutes, and a pool thread
            // parked that long starves the task workers that park on the pool too.
            return await Task.Factory.StartNew(
                () => Lend(borrower),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        finally
        {
            _lendGate.Release();
        }
    }

    private T Lend<T>(Func<T> borrower)
    {
        lock (_sync)
        {
            _isLent = true;
            var deadline = DateTime.UtcNow + UseDrainTimeout;
            while (_users > 0 && DateTime.UtcNow < deadline)
            {
                Monitor.Wait(_sync, TimeSpan.FromMilliseconds(50));
            }
        }

        IConsoleHandoverParticipant[] participants;
        lock (_participants)
        {
            participants = [.. _participants];
        }

        try
        {
            foreach (var participant in participants)
            {
                Try(participant.OnConsoleReleased);
            }

            return borrower();
        }
        finally
        {
            // In reverse, so whatever was released last — the screen, typically — comes back first.
            for (var i = participants.Length - 1; i >= 0; i--)
            {
                Try(participants[i].OnConsoleReacquired);
            }

            lock (_sync)
            {
                _isLent = false;
            }

            Try(() => Reacquired?.Invoke(this, EventArgs.Empty));
        }
    }

    /// <summary>
    /// Runs <paramref name="startInfo"/> on the lent terminal — inheriting it, so nothing may be
    /// redirected — and returns its exit code. Ctrl+C and friends belong to the child meanwhile
    /// (<see cref="ConsoleInterruptShield"/>). Throws when the program cannot be started at all.
    /// </summary>
    public Task<int> RunInteractiveAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default) =>
        LendAsync(() => RunInherited(startInfo), cancellationToken);

    /// <summary>
    /// The inside of <see cref="RunInteractiveAsync"/>, for a borrower that does more on the lent
    /// terminal than run one program (a banner first, a pause after). Call it only from inside
    /// <see cref="LendAsync{T}"/>.
    /// </summary>
    public static int RunInherited(ProcessStartInfo startInfo)
    {
        if (startInfo.RedirectStandardInput || startInfo.RedirectStandardOutput || startInfo.RedirectStandardError)
        {
            throw new ArgumentException("An interactive process has to inherit the terminal: nothing may be redirected.", nameof(startInfo));
        }

        startInfo.UseShellExecute = false;
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{startInfo.FileName} did not start.");

        // Only now: the shield is inherited by anything started while it is up.
        using (ConsoleInterruptShield.Raise())
        {
            process.WaitForExit();
        }

        return process.ExitCode;
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            // A participant that cannot undo its state must not keep the terminal from coming back.
            Debug.WriteLine($"Console handover: {exception}");
        }
    }
}
