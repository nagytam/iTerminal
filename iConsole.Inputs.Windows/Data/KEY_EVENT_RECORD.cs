using System.Diagnostics;
using System.Runtime.InteropServices;

namespace iConsole.Inputs.Windows.Data;

[DebuggerDisplay("KeyCode: {wVirtualKeyCode}")]
[StructLayout(LayoutKind.Explicit)]
public struct KEY_EVENT_RECORD
{
    public const int CAPSLOCK_ON        = 0x0080; // The CAPS LOCK light is on.
    public const int ENHANCED_KEY       = 0x0100; // The key is enhanced.See remarks.
    public const int LEFT_ALT_PRESSED   = 0x0002; // The left ALT key is pressed.
    public const int LEFT_CTRL_PRESSED  = 0x0008; // The left CTRL key is pressed.
    public const int NUMLOCK_ON         = 0x0020; // The NUM LOCK light is on.
    public const int RIGHT_ALT_PRESSED  = 0x0001; // The right ALT key is pressed.
    public const int RIGHT_CTRL_PRESSED = 0x0004; // The right CTRL key is pressed.
    public const int SCROLLLOCK_ON      = 0x0040; // The SCROLL LOCK light is on.
    public const int SHIFT_PRESSED      = 0x0010; // The SHIFT key is pressed.
    
    [FieldOffset(0)]
    [MarshalAs(UnmanagedType.Bool)]
    public bool bKeyDown;
    [FieldOffset(4)]
    public ushort wRepeatCount;
    [FieldOffset(6)]
    public ushort wVirtualKeyCode;
    [FieldOffset(8)]
    public ushort wVirtualScanCode;
    [FieldOffset(10)]
    public char UnicodeChar;
    [FieldOffset(10)]
    public byte AsciiChar;
    [FieldOffset(12)]
    public int dwControlKeyState;
};
