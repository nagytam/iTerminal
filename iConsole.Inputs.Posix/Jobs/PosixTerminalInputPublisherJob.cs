using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using iConsole.Data;
using iConsole.Graphics;
using iConsole.Inputs;
using iConsole.Inputs.Posix.Interop;
using iConsole.Jobs;
using iConsole.Services;

namespace iConsole.Inputs.Posix.Jobs;

/// <summary>
/// Linux: raw stdin + SGR mouse (shared <see cref="TerminalInputUtf8Decoder"/> / <see cref="TerminalMouseAnsi"/>).
/// Same <see cref="ITerminalInputSource"/> shape as Windows <c>MouseEventPublisherJob</c> for a single consumer.
/// </summary>
public sealed class PosixTerminalInputPublisherJob : Job, ITerminalInputSource, IConsoleHandoverParticipant
{
    private readonly ConsoleOwnership? _consoleOwnership;

    /// <summary>Whether a lend took raw mode and mouse reporting down, so taking the terminal back puts them up again.</summary>
    private bool _releasedRawMode;

    public PosixTerminalInputPublisherJob(ConsoleOwnership? consoleOwnership = null)
    {
        _consoleOwnership = consoleOwnership;
        _consoleOwnership?.AddParticipant(this);
    }

    public ConcurrentQueue<MouseEvent> MouseQueue { get; } = new();
    public ConcurrentQueue<KeyEvent> KeyQueue { get; } = new();
    public SemaphoreSlim InputAvailableSignal { get; } = new(0);
    public override JobPriority Priority => JobPriority.Input;

    private readonly TerminalInputUtf8Decoder _decoder = new();

    /// <summary>Whether focus reports were asked for, so they are switched off before the shell gets the terminal back.</summary>
    private bool _focusReporting;

    private void EnableFocusReporting()
    {
        if (_focusReporting || !TerminalFocusReporting.IsEnabledByEnvironment)
        {
            return;
        }

        Console.Out.Write(TerminalFocusReporting.Enable);
        _focusReporting = true;
    }

    /// <summary>Asks the terminal how large a cell is in pixels, so a Sixel picture can be sized to the cells it covers. The answer is read with the keys.</summary>
    private static void QueryCellSize()
    {
        if (TerminalGraphics.Current.IsSixelSupported && !TerminalGraphics.Current.IsCellSizeKnown)
        {
            Console.Out.Write("\u001b[16t");
        }
    }

    private void DisableFocusReporting()
    {
        if (!_focusReporting)
        {
            return;
        }

        try
        {
            Console.Out.Write(TerminalFocusReporting.Disable);
            Console.Out.Flush();
        }
        catch
        {
        }

        _focusReporting = false;
    }
    private LinuxStdin.Termios _original;
    private bool _termiosCaptured;
    private bool _mouseEnabled;

    /// <summary>Whether the Kitty keyboard flags are pushed on the terminal, so they must be popped before the shell gets it back.</summary>
    private bool _kittyPushed;

    private void PushKittyKeyboard()
    {
        if (_kittyPushed || !KittyKeyboardProtocol.IsEnabledByEnvironment)
        {
            return;
        }

        Console.Out.Write(KittyKeyboardProtocol.Push);
        Console.Out.Flush();
        _kittyPushed = true;
    }

    private void PopKittyKeyboard()
    {
        if (!_kittyPushed)
        {
            return;
        }

        try
        {
            Console.Out.Write(KittyKeyboardProtocol.Pop);
            Console.Out.Flush();
        }
        catch
        {
        }

        _kittyPushed = false;
    }

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return Task.CompletedTask;
        }

        try
        {
            if (!TryEnterRawMode())
            {
                return Task.CompletedTask;
            }

            Console.Out.Write(TerminalMouseAnsi.EnablePreferredMouseReporting);
            EnableFocusReporting();
            _decoder.FocusReported += TerminalFocus.Current.Report;
            _decoder.CellSizeReported += (width, height) => TerminalGraphics.Current.SetCellSize(width, height);
            QueryCellSize();
            if (KittyKeyboardProtocol.IsEnabledByEnvironment)
            {
                Console.Out.Write(KittyKeyboardProtocol.Query);
            }

            Console.Out.Flush();
            _mouseEnabled = true;

            if (!TrySetNonBlockingStdin(true))
            {
                return Task.CompletedTask;
            }

            var buffer = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                // Lent to a child process: stdin is the child's to read.
                if (_consoleOwnership is not null && !_consoleOwnership.TryBeginUse())
                {
                    Thread.Sleep(10);
                    continue;
                }

                bool keepReading;
                try
                {
                    keepReading = ReadOnePass(buffer);
                }
                finally
                {
                    _consoleOwnership?.EndUse();
                }

                if (!keepReading)
                {
                    break;
                }
            }
        }
        finally
        {
            PopKittyKeyboard();
            DisableFocusReporting();
            if (_mouseEnabled)
            {
                try
                {
                    Console.Out.Write(TerminalMouseAnsi.DisablePreferredMouseReporting);
                    Console.Out.Flush();
                }
                catch
                {
                }

                _mouseEnabled = false;
            }

            _ = TrySetNonBlockingStdin(false);
            _ = TryRestoreTermios();
        }

        return Task.CompletedTask;
    }

    /// <summary>One non-blocking read of stdin, decoded into the queues; false when stdin has failed for good.</summary>
    private bool ReadOnePass(byte[] buffer)
    {
        int n;
        unsafe
        {
            fixed (byte* p = buffer)
            {
                n = LinuxStdin.read(LinuxStdin.StdinFd, p, (nuint)buffer.Length);
            }
        }

        if (n < 0)
        {
            if (Marshal.GetLastPInvokeError() == LinuxStdin.Eagain)
            {
                _decoder.DrainStandaloneEscape(k => { KeyQueue.Enqueue(k); InputAvailableSignal.Release(); });
                Thread.Sleep(1);
                return true;
            }

            return false;
        }

        if (n == 0)
        {
            _decoder.DrainStandaloneEscape(k => { KeyQueue.Enqueue(k); InputAvailableSignal.Release(); });
            Thread.Sleep(1);
            return true;
        }

        _decoder.Append(buffer.AsSpan(0, n));
        _decoder.Drain(
            k =>
            {
                KeyQueue.Enqueue(k);
                InputAvailableSignal.Release();
            },
            m =>
            {
                MouseQueue.Enqueue(m);
                InputAvailableSignal.Release();
            });

        // The terminal answered the query: only now is it safe to ask for the new key format.
        if (_decoder.KittyKeyboardSupported)
        {
            PushKittyKeyboard();
        }

        return true;
    }

    /// <summary>
    /// Cooked mode with echo and signals, blocking stdin and no mouse reports: the terminal as the
    /// shell that launched the app left it, which is what the child expects to inherit.
    /// </summary>
    public void OnConsoleReleased()
    {
        if (!_termiosCaptured)
        {
            return;
        }

        PopKittyKeyboard();
        DisableFocusReporting();
        if (_mouseEnabled)
        {
            Console.Out.Write(TerminalMouseAnsi.DisablePreferredMouseReporting);
            Console.Out.Flush();
            _mouseEnabled = false;
        }

        _ = TrySetNonBlockingStdin(false);
        _ = TryRestoreTermios();
        _releasedRawMode = true;
    }

    /// <summary>Raw mode again — <c>TCSAFLUSH</c> also drops anything typed into the child and not read — then mouse reports and non-blocking reads.</summary>
    public void OnConsoleReacquired()
    {
        if (!_releasedRawMode)
        {
            return;
        }

        _releasedRawMode = false;
        if (TryEnterRawMode())
        {
            Console.Out.Write(TerminalMouseAnsi.EnablePreferredMouseReporting);
            EnableFocusReporting();
            QueryCellSize();
            Console.Out.Flush();
            _mouseEnabled = true;
            if (_decoder.KittyKeyboardSupported)
            {
                PushKittyKeyboard();
            }

            _ = TrySetNonBlockingStdin(true);
        }
    }

    private bool TryEnterRawMode()
    {
        _original.c_cc = new byte[32];
        if (LinuxStdin.TcGetAttr(LinuxStdin.StdinFd, ref _original) != 0)
        {
            return false;
        }

        _termiosCaptured = true;
        var raw = new LinuxStdin.Termios
        {
            c_iflag = _original.c_iflag,
            c_oflag = _original.c_oflag,
            c_cflag = _original.c_cflag,
            c_lflag = _original.c_lflag,
            c_line = _original.c_line,
            c_ispeed = _original.c_ispeed,
            c_ospeed = _original.c_ospeed,
            c_cc = new byte[32],
        };
        if (_original.c_cc is not null)
        {
            Array.Copy(_original.c_cc, raw.c_cc, Math.Min(_original.c_cc.Length, 32));
        }

        raw.c_lflag &= ~(LinuxStdin.ICanon | LinuxStdin.Echo | LinuxStdin.ISig);
        raw.c_cc[LinuxStdin.VMin] = 0;
        raw.c_cc[LinuxStdin.VTime] = 0;
        return LinuxStdin.TcSetAttr(LinuxStdin.StdinFd, LinuxStdin.TcsaFlush, ref raw) == 0;
    }

    private bool TryRestoreTermios()
    {
        if (!_termiosCaptured)
        {
            return true;
        }

        _termiosCaptured = false;
        return LinuxStdin.TcSetAttr(LinuxStdin.StdinFd, LinuxStdin.TcsaNow, ref _original) == 0;
    }

    private bool TrySetNonBlockingStdin(bool enable)
    {
        var flags = LinuxStdin.fcntl(LinuxStdin.StdinFd, LinuxStdin.FGetFl, 0);
        if (flags < 0)
        {
            return false;
        }

        var next = enable ? flags | LinuxStdin.ONonBlock : flags & ~LinuxStdin.ONonBlock;
        return LinuxStdin.fcntl(LinuxStdin.StdinFd, LinuxStdin.FSetFl, next) >= 0;
    }
}
