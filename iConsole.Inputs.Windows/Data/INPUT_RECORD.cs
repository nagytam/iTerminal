using System.Diagnostics;
using System.Runtime.InteropServices;

namespace iConsole.Inputs.Windows.Data;

[DebuggerDisplay("EventType: {EventType}")]
[StructLayout(LayoutKind.Explicit)]
public struct INPUT_RECORD
{
    [FieldOffset(0)]
    public short EventType;
    [FieldOffset(4)]
    public KEY_EVENT_RECORD KeyEvent;
    [FieldOffset(4)]
    public MOUSE_EVENT_RECORD MouseEvent;
}
