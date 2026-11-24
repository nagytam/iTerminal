using System.Diagnostics;
using System.Runtime.InteropServices;

namespace iConsole.Inputs.Windows.Data;

#pragma warning disable S101, S1104 // Win32 ABI: the native names and the public field layout are part of the interop contract.
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
    [FieldOffset(4)]
    public int SetFocus;
}
#pragma warning restore S101, S1104
