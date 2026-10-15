using iConsole.Data;
using System.Runtime.InteropServices;

namespace iConsole.Drivers;

public static class ConsoleWindowSizeHelper
{
    public static Size GetVisibleWindowSize()
    {
        if (OperatingSystem.IsWindows() && TryGetVisibleWindowInfo(out var visibleWindowSize, out _))
        {
            return visibleWindowSize;
        }

        try
        {
            var size = Normalize(new Size(Console.WindowWidth, Console.WindowHeight));
            return size.Width > 0 && size.Height > 0
                ? size
                : defaultSize;
        }
        catch (IOException)
        {
            // No console attached in this environment - fall back to a sensible terminal size.
            return defaultSize;
        }
        catch (ArgumentOutOfRangeException)
        {
            return defaultSize;
        }
    }

    public static Position GetVisibleWindowPosition()
    {
        if (OperatingSystem.IsWindows() && TryGetVisibleWindowInfo(out _, out var visibleWindowPosition))
        {
            return visibleWindowPosition;
        }

        return new Position(0, 0);
    }

    public static Size Normalize(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return size;
        }

        try
        {
            var bufferWidth = Console.BufferWidth;
            var bufferHeight = Console.BufferHeight;
            var windowWidth = Console.WindowWidth;
            var windowHeight = Console.WindowHeight;

            if (windowWidth > 0)
            {
                size.Width = Math.Min(size.Width, windowWidth);
            }

            if (windowHeight > 0)
            {
                size.Height = Math.Min(size.Height, windowHeight);
            }
            else if (bufferHeight > 0)
            {
                size.Height = Math.Min(size.Height, bufferHeight);
            }

            if (bufferWidth > 0)
            {
                size.Width = Math.Min(size.Width, bufferWidth);
            }
        }
        catch (IOException)
        {
        }

        return size;
    }

    public static Position GetBufferPosition(Position visibleWindowPosition, Position viewportOrigin)
    {
        return new Position(visibleWindowPosition.X + viewportOrigin.X, visibleWindowPosition.Y + viewportOrigin.Y);
    }

    public static Position GetVisiblePosition(Position bufferPosition, Position viewportOrigin)
    {
        return new Position(
            Math.Max(0, bufferPosition.X - viewportOrigin.X),
            Math.Max(0, bufferPosition.Y - viewportOrigin.Y));
    }

    private static bool TryGetVisibleWindowInfo(out Size size, out Position position)
    {
        var handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (handle == IntPtr.Zero || handle == INVALID_HANDLE_VALUE)
        {
            size = defaultSize;
            position = defaultPosition;
            return false;
        }

        if (!GetConsoleScreenBufferInfo(handle, out var consoleScreenBufferInfo))
        {
            size = defaultSize;
            position = defaultPosition;
            return false;
        }

        var width = consoleScreenBufferInfo.srWindow.Right - consoleScreenBufferInfo.srWindow.Left + 1;
        var height = consoleScreenBufferInfo.srWindow.Bottom - consoleScreenBufferInfo.srWindow.Top + 1;
        position = new Position(consoleScreenBufferInfo.srWindow.Left, consoleScreenBufferInfo.srWindow.Top);
        size = Normalize(new Size(width, height));
        return size.Width > 0 && size.Height > 0;
    }

    private const int STD_OUTPUT_HANDLE = -11;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
    private static readonly Size defaultSize = new(80, 24);
    private static readonly Position defaultPosition = new(0, 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SMALL_RECT
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD dwSize;
        public COORD dwCursorPosition;
        public short wAttributes;
        public SMALL_RECT srWindow;
        public COORD dwMaximumWindowSize;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);
}
