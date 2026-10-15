using Microsoft.Win32.SafeHandles;

namespace iConsole.Inputs.Windows.Data;

public class ConsoleHandle : SafeHandleMinusOneIsInvalid
{
    public ConsoleHandle() : base(false) { }

    protected override bool ReleaseHandle()
    {
        return true; // TODO release handle
    }
}

