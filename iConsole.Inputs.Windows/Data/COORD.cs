using System.Diagnostics;

namespace iConsole.Inputs.Windows.Data;

#pragma warning disable S101, S1104 // Win32 ABI: the native names and the public field layout are part of the interop contract.
[DebuggerDisplay("{X}, {Y}")]
public struct COORD
{
    public ushort X;
    public ushort Y;
}
#pragma warning restore S101, S1104
