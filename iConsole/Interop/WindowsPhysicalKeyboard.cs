using System.Runtime.InteropServices;

namespace iConsole.Interop;

/// <summary>
/// Reads physical modifier key state (Windows). Used when console input does not emit modifier-only key events (e.g. <see cref="Jobs.KeyboardEventPublisherJob"/>).
/// </summary>
public static class WindowsPhysicalKeyboard
{
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    /// <summary>True if either Shift key is held.</summary>
    public static bool IsShiftHeld() =>
        OperatingSystem.IsWindows()
        && (IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT));

    /// <summary>True if either Control key is held.</summary>
    public static bool IsControlHeld() =>
        OperatingSystem.IsWindows()
        && (IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL));

    /// <summary>True if either Alt key is held.</summary>
    public static bool IsAltHeld() =>
        OperatingSystem.IsWindows()
        && (IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU));

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32", EntryPoint = "GetAsyncKeyState", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int virtualKey);
}
