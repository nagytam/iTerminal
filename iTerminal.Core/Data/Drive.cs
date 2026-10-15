using iConsole.Data;
using System.Diagnostics;

namespace iTerminal.Core.Data;

[DebuggerDisplay("{Name}")]
public class Drive
{
    public DriveInfo? DriveInfo { get; set; }
    public string Name { get; set; } = string.Empty;
    public Url Url { get; set; }

    public bool IsEnabled
    {
        get
        {
            if (   (Name.StartsWith(Container.MEMORY_DRIVE)) 
                || (DriveInfo != null && DriveInfo.IsReady))
            {
                return true;
            }
            return false;
        }
    }
}
