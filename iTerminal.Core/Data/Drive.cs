using iConsole.Data;
using System.Diagnostics;

namespace iTerminal.Core.Data;

[DebuggerDisplay("{Name}")]
public class Drive
{
    /// <summary>
    /// Width of the name column at the start of <see cref="Name"/>. The list row puts the usage bar right after
    /// it, so the drive letters stay in one column; the job pads the name to this width.
    /// </summary>
    public const int NameColumnWidth = 20;

    public DriveInfo? DriveInfo { get; set; }
    public string Name { get; set; } = string.Empty;
    public Url Url { get; set; } = null!;

    /// <summary>
    /// How full the volume is, 0 to 1, read by the job when the drive is ready; null when it is not known
    /// (no media, a cloud or memory drive, or a volume that reports no size).
    /// </summary>
    public double? UsedFraction { get; set; }

    /// <summary>Used share of a volume, from its total size and its free space; null when there is no size to divide by.</summary>
    public static double? UsedFractionOf(DriveInfo? info)
    {
        if (info is null || !info.IsReady || info.TotalSize <= 0)
        {
            return null;
        }
        var used = (double)(info.TotalSize - info.TotalFreeSpace) / info.TotalSize;
        return Math.Clamp(used, 0, 1);
    }

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
