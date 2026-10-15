using iConsole.Controls.Buttons;
using System.Diagnostics;

namespace iConsole.Inputs.Windows.Data;

[DebuggerDisplay("{dwMousePosition.X}, {dwMousePosition.Y}")]
public struct MOUSE_EVENT_RECORD
{
    public const int FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001; // The leftmost mouse button.
    public const int FROM_LEFT_2ND_BUTTON_PRESSED = 0x0004; // The second button fom the left.
    public const int FROM_LEFT_3RD_BUTTON_PRESSED = 0x0008; // The third button from the left.
    public const int FROM_LEFT_4TH_BUTTON_PRESSED = 0x0010; // The fourth button from the left.
    public const int RIGHTMOST_BUTTON_PRESSED     = 0x0002; // The rightmost button from the left.
    public const int MouseMoved = 0x0001;
    public const int DoubleClick = 0x0002;
    public const int MouseWheeled = 0x0004;
    public const int MouseHorizontalWheeled = 0x0008;

    public COORD dwMousePosition;
    public int dwButtonState;
    public int dwControlKeyState;
    public int dwEventFlags;
}

 