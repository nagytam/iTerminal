namespace iTerminal.Core.Data;

/// <summary>
/// Lightweight description of a connected iOS device as surfaced by the iPhone browser dialog.
/// <see cref="Udid"/> is the stable identifier used to build <c>iphone://&lt;udid&gt;/</c> URLs.
/// </summary>
#pragma warning disable S101 // The name is the product's own naming (iTerminal, iPhone) or the Win32 name the structure mirrors; renaming it would hide the link.
public sealed record IPhoneDeviceInfo(string Udid, string Name, string ProductType)
#pragma warning restore S101
{
    /// <summary>Label shown in the device list, e.g. <c>"Tamas's iPhone (iPhone14,2)"</c>.</summary>
    public string DisplayName =>
        string.IsNullOrEmpty(ProductType) ? Name : $"{Name} ({ProductType})";
}
