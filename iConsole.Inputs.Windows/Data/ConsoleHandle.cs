using Microsoft.Win32.SafeHandles;

namespace iConsole.Inputs.Windows.Data;

public class ConsoleHandle : SafeHandleMinusOneIsInvalid
{
    public ConsoleHandle() : base(false) { }

    protected override bool ReleaseHandle()
    {
        return true; // Not owned: the constructor passes ownsHandle = false, so there is nothing to release.
    }
}

