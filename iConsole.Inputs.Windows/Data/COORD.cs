using System.Diagnostics;

namespace iConsole.Inputs.Windows.Data;

[DebuggerDisplay("{X}, {Y}")]
public struct COORD
{
    public ushort X;
    public ushort Y;
}
