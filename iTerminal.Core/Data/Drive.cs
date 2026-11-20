using iConsole.Data;
using System.Diagnostics;

namespace iTerminal.Core.Data;

[DebuggerDisplay("{Name}")]
public class Drive
{
    public DriveInfo? DriveInfo { get; set; }
    public string Name { get; set; } = string.Empty;
    public Url Url { get; set; } = null!;

    /// <summary>
    /// Whether the app registration of this cloud drive is set in the configuration. Only a cloud row
    /// can be enabled by it; it defaults to false, so a row is greyed out until the job says otherwise.
    /// </summary>
    public bool IsCloudConfigured { get; set; }

    /// <summary>
    /// True for the OneDrive and Google Drive rows. It is read from the URL, not the name: the name carries
    /// a "not configured" suffix, and a row built without a URL is not a cloud drive.
    /// </summary>
    public bool IsCloud => Url?.ContainerType is ContainerType.OneDrive or ContainerType.GoogleDrive;

    public bool IsEnabled
    {
        get
        {
            if (   (Name.StartsWith(Container.MEMORY_DRIVE))
                || (IsCloud && IsCloudConfigured)
                || (DriveInfo != null && DriveInfo.IsReady))
            {
                return true;
            }
            return false;
        }
    }
}
