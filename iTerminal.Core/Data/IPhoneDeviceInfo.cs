namespace iTerminal.Core.Data;

/// <summary>
/// Lightweight description of a connected iOS device as surfaced by the iPhone browser dialog.
/// <see cref="Udid"/> is the stable identifier used to build <c>iphone://&lt;udid&gt;/</c> URLs.
/// </summary>
public sealed record IPhoneDeviceInfo(string Udid, string Name, string ProductType)
{
    /// <summary>Label shown in the device list, e.g. <c>"Tamas's iPhone (iPhone14,2)"</c>.</summary>
    public string DisplayName =>
        string.IsNullOrEmpty(ProductType) ? Name : $"{Name} ({ProductType})";
}
