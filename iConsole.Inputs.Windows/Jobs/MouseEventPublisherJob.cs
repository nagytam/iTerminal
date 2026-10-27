using iConsole.Data;
using iConsole.Inputs;
using iConsole.Inputs.Windows.Data;
using iConsole.Jobs;
using iConsole.Services;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace iConsole.Inputs.Windows.Jobs;

public partial class MouseEventPublisherJob : Job, ITerminalInputSource, IConsoleHandoverParticipant
{
    private readonly ConsoleHandle _consoleHandle;
    private readonly ConsoleOwnership? _consoleOwnership;

    /// <summary>The input mode the console had before this job changed it — what a child process gets while it has the console.</summary>
    private readonly int _originalMode;

    /// <summary>The mode this job reads in: mouse input on, quick edit off.</summary>
    private readonly int _applicationMode;
    public ConcurrentQueue<MouseEvent> MouseQueue { get; } = new();
    public ConcurrentQueue<KeyEvent> KeyQueue { get; } = new();
    public SemaphoreSlim InputAvailableSignal { get; } = new(0);
    public override JobPriority Priority => JobPriority.Input;

    public MouseEventPublisherJob(ConsoleOwnership? consoleOwnership = null)
    {
        _consoleHandle = NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE);

        int mode = 0;
        if (!(NativeMethods.GetConsoleMode(_consoleHandle, ref mode))) 
        { 
            throw new Win32Exception(); 
        }

        _originalMode = mode;

        mode |= NativeMethods.ENABLE_MOUSE_INPUT;
        mode &= ~NativeMethods.ENABLE_QUICK_EDIT_MODE;
        mode |= NativeMethods.ENABLE_EXTENDED_FLAGS;
        mode &= ~NativeMethods.KEY_EVENT;

        if (!(NativeMethods.SetConsoleMode(_consoleHandle, mode))) 
        { 
            throw new Win32Exception(); 
        }

        _applicationMode = mode;
        _consoleOwnership = consoleOwnership;
        _consoleOwnership?.AddParticipant(this);
    }

    /// <summary>Line input, echo and quick edit as the console had them: a shell reads keys its own way.</summary>
    public void OnConsoleReleased()
    {
        NativeMethods.SetConsoleMode(_consoleHandle, _originalMode);
    }

    /// <summary>
    /// The app's mode again — the child may have left virtual-terminal input or raw mode on — and
    /// nothing typed into the child is left over for the panels: the Enter that ended "exit" is
    /// still releasing as the console comes back.
    /// </summary>
    public void OnConsoleReacquired()
    {
        NativeMethods.SetConsoleMode(_consoleHandle, _applicationMode);
        NativeMethods.FlushConsoleInputBuffer(_consoleHandle);
    }

    // ReadConsoleInput blocks until an input record actually arrives and does not observe a
    // CancellationToken, so pressing F10 (Quit) could leave this thread parked in that call
    // until another key/mouse event happened to wake it up, stalling app shutdown. Waiting on
    // the console handle first with a short timeout lets the loop re-check cancellationToken
    // frequently while still only calling the (now effectively non-blocking) read once input
    // is actually pending.
    private const uint PollIntervalMs = 50;

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Lent to a child process: its keys are not this job's to read.
            if (_consoleOwnership is not null && !_consoleOwnership.TryBeginUse())
            {
                Thread.Sleep(10);
                continue;
            }

            try
            {
                ReadOnePass();
            }
            finally
            {
                _consoleOwnership?.EndUse();
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>One wait of at most <see cref="PollIntervalMs"/> for input, and the record that arrived, if any.</summary>
    private void ReadOnePass()
    {
        if (OperatingSystem.IsWindows())
        {
            var waitResult = NativeMethods.WaitForSingleObject(_consoleHandle, PollIntervalMs);
            if (waitResult != NativeMethods.WAIT_OBJECT_0)
            {
                return;
            }

            var record = new INPUT_RECORD();
            uint recordLen = 0;
            if (!(NativeMethods.ReadConsoleInput(_consoleHandle, ref record, 1, ref recordLen)))
            {
                throw new Win32Exception();
            }
            switch (record.EventType)
            {
                case NativeMethods.MOUSE_EVENT:
                    {
                        var mouseEvent = MapMouseEvent(record.MouseEvent);

                        MouseQueue.Enqueue(mouseEvent);
                        InputAvailableSignal.Release();
                        break;
                    }
                case NativeMethods.FOCUS_EVENT:
                    {
                        iConsole.Services.TerminalFocus.Current.Report(record.SetFocus != 0);
                        break;
                    }
                case NativeMethods.KEY_EVENT:
                    {
                        if (record.KeyEvent.AsciiChar == '\0')
                        {
                            var mappedKey = MapConsoleKey(record.KeyEvent.wVirtualKeyCode);
                            var keyEvent = new KeyEvent(
                                ' ', mappedKey,
                                IsShiftKeyPressed(record.KeyEvent.dwControlKeyState),
                                IsAltKeyPressed(record.KeyEvent.dwControlKeyState),
                                IsCtrlKeyPressed(record.KeyEvent.dwControlKeyState),
                                (record.KeyEvent.bKeyDown) ? KeyPressType.Down : KeyPressType.Up
                                );
                            KeyQueue.Enqueue(keyEvent);
                            InputAvailableSignal.Release();
                        }
                        else if (record.KeyEvent.bKeyDown)
                        {
                            var keyEvent = new KeyEvent(
                                record.KeyEvent.UnicodeChar,
                                MapConsoleKey(record.KeyEvent.wVirtualKeyCode),
                                IsShiftKeyPressed
                                (record.KeyEvent.dwControlKeyState),
                                IsAltKeyPressed
                                (record.KeyEvent.dwControlKeyState),
                                IsCtrlKeyPressed
                                (record.KeyEvent.dwControlKeyState));

                            KeyQueue.Enqueue(keyEvent);
                            InputAvailableSignal.Release();
                        }
                        break;
                    }
            }
        }
        else
        {
            Thread.Sleep((int)PollIntervalMs);
        }
    }

    internal static MouseEvent MapMouseEvent(MOUSE_EVENT_RECORD record)
    {
        var eventFlags = record.dwEventFlags;
        var buttonState = record.dwButtonState;
        var isMotion = (eventFlags & MOUSE_EVENT_RECORD.MouseMoved) == MOUSE_EVENT_RECORD.MouseMoved;
        var isWheel = (eventFlags & MOUSE_EVENT_RECORD.MouseWheeled) == MOUSE_EVENT_RECORD.MouseWheeled;
        var isHorizontalWheel = (eventFlags & MOUSE_EVENT_RECORD.MouseHorizontalWheeled) == MOUSE_EVENT_RECORD.MouseHorizontalWheeled;
        var isButtonTransition = eventFlags == 0 || (eventFlags & MOUSE_EVENT_RECORD.DoubleClick) == MOUSE_EVENT_RECORD.DoubleClick;

        return new MouseEvent
        {
            Position = new Position(record.dwMousePosition.X, record.dwMousePosition.Y),
            LeftClicked = isButtonTransition
                && !isMotion
                && !isWheel
                && (buttonState & MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED) == MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED,
            RightClicked = isButtonTransition
                && !isMotion
                && !isWheel
                && (buttonState & MOUSE_EVENT_RECORD.RIGHTMOST_BUTTON_PRESSED) == MOUSE_EVENT_RECORD.RIGHTMOST_BUTTON_PRESSED,
            IsRelease = isButtonTransition && buttonState == 0,
            IsDrag = isMotion
                && !isWheel
                && (buttonState & MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED) == MOUSE_EVENT_RECORD.FROM_LEFT_1ST_BUTTON_PRESSED,
            WheelDelta = isWheel && !isHorizontalWheel ? GetWheelDelta(buttonState) : 0,
            IsHover = isMotion
                && !isWheel
                && buttonState == 0,
        };
    }

    private static int GetWheelDelta(int buttonState)
    {
        var delta = unchecked((short)((buttonState >> 16) & 0xffff));
        if (delta == 0)
        {
            return 0;
        }

        return Math.Sign(delta);
    }

    private static bool IsShiftKeyPressed(int dwControlKeyState)
    {
        return (dwControlKeyState & KEY_EVENT_RECORD.SHIFT_PRESSED) == KEY_EVENT_RECORD.SHIFT_PRESSED;
    }

    private static bool IsAltKeyPressed(int dwControlKeyState)
    {
        return ((dwControlKeyState & KEY_EVENT_RECORD.LEFT_ALT_PRESSED) == KEY_EVENT_RECORD.LEFT_ALT_PRESSED) ||
               ((dwControlKeyState & KEY_EVENT_RECORD.RIGHT_ALT_PRESSED) == KEY_EVENT_RECORD.RIGHT_ALT_PRESSED);
    }

    private static bool IsCtrlKeyPressed(int dwControlKeyState)
    {
        return ((dwControlKeyState & KEY_EVENT_RECORD.LEFT_CTRL_PRESSED) == KEY_EVENT_RECORD.LEFT_CTRL_PRESSED) ||
               ((dwControlKeyState & KEY_EVENT_RECORD.RIGHT_CTRL_PRESSED) == KEY_EVENT_RECORD.RIGHT_CTRL_PRESSED);
    }

    private static ConsoleKey MapConsoleKey(ushort wVirtualKeyCode)
    {
        VKey virtualKey = (VKey)wVirtualKeyCode;
        switch (virtualKey)
        {
            case VKey.F1: return ConsoleKey.F1;
            case VKey.F2: return ConsoleKey.F2;
            case VKey.F3: return ConsoleKey.F3;
            case VKey.F4: return ConsoleKey.F4;
            case VKey.F5: return ConsoleKey.F5;
            case VKey.F6: return ConsoleKey.F6;
            case VKey.F7: return ConsoleKey.F7;
            case VKey.F8: return ConsoleKey.F8;
            case VKey.F9: return ConsoleKey.F9;
            case VKey.F10: return ConsoleKey.F10;
            case VKey.F11: return ConsoleKey.F11;
            case VKey.F12: return ConsoleKey.F12;
            case VKey.ESCAPE: return ConsoleKey.Escape;
            case VKey.HOME: return ConsoleKey.Home;
            case VKey.END: return ConsoleKey.End;
            case VKey.SPACE: return ConsoleKey.Spacebar;
            case VKey.RETURN: return ConsoleKey.Enter;
            case VKey.UP: return ConsoleKey.UpArrow;
            case VKey.DOWN: return ConsoleKey.DownArrow;
            case VKey.PRIOR: return ConsoleKey.PageUp;
            case VKey.NEXT: return ConsoleKey.PageDown;
            case VKey.LEFT: return ConsoleKey.LeftArrow;
            case VKey.RIGHT: return ConsoleKey.RightArrow;
            case VKey.CONTROL: return ConsoleKey.None;
            case VKey.LCONTROL: return ConsoleKey.None;
            case VKey.RCONTROL: return ConsoleKey.None;
            case VKey.SHIFT: return ConsoleKey.None;
            case VKey.LSHIFT: return ConsoleKey.None;
            case VKey.RSHIFT: return ConsoleKey.None;
            case VKey.MENU: return ConsoleKey.None;
            default:
                {
                    break;
                }
        }
        return ConsoleKey.None;
    }
}
