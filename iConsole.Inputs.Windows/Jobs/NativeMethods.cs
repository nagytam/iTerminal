using System.Runtime.InteropServices;
using iConsole.Inputs.Windows.Data;

namespace iConsole.Inputs.Windows.Jobs;

internal partial class NativeMethods
{
    public const Int32 STD_INPUT_HANDLE = -10;

    public const Int32 ENABLE_MOUSE_INPUT = 0x0010;
    public const Int32 ENABLE_QUICK_EDIT_MODE = 0x0040;
    public const Int32 ENABLE_EXTENDED_FLAGS = 0x0080;

    public const Int32 KEY_EVENT = 1;
    public const Int32 MOUSE_EVENT = 2;

    [DllImportAttribute("kernel32.dll", SetLastError = true)]
    [return: MarshalAsAttribute(UnmanagedType.Bool)]
    public static extern Boolean GetConsoleMode(ConsoleHandle hConsoleHandle, ref Int32 lpMode);

    [DllImportAttribute("kernel32.dll", SetLastError = true)]
    public static extern ConsoleHandle GetStdHandle(Int32 nStdHandle);

    [DllImportAttribute("kernel32.dll", SetLastError = true)]
    [return: MarshalAsAttribute(UnmanagedType.Bool)]
    public static extern Boolean ReadConsoleInput(ConsoleHandle hConsoleInput, ref INPUT_RECORD lpBuffer, UInt32 nLength, ref UInt32 lpNumberOfEventsRead);

    [DllImportAttribute("kernel32.dll", SetLastError = true)]
    [return: MarshalAsAttribute(UnmanagedType.Bool)]
    public static extern Boolean SetConsoleMode(ConsoleHandle hConsoleHandle, Int32 dwMode);

    // Drops whatever is queued — keys typed into a child process that had the console, still
    // releasing as the console comes back to the app.
    [DllImportAttribute("kernel32.dll", SetLastError = true)]
    [return: MarshalAsAttribute(UnmanagedType.Bool)]
    public static extern Boolean FlushConsoleInputBuffer(ConsoleHandle hConsoleInput);

    public const UInt32 WAIT_OBJECT_0 = 0x00000000;
    public const UInt32 WAIT_TIMEOUT = 0x00000102;
    public const UInt32 WAIT_FAILED = 0xFFFFFFFF;

    // Lets the read loop poll for pending input with a short timeout instead of blocking
    // indefinitely in ReadConsoleInput, so it notices cancellation promptly on shutdown.
    [DllImportAttribute("kernel32.dll", SetLastError = true)]
    public static extern UInt32 WaitForSingleObject(ConsoleHandle hHandle, UInt32 dwMilliseconds);
}
