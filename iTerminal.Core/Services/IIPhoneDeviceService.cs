using iTerminal.Core.Data;

namespace iTerminal.Core.Services;

/// <summary>
/// Enumerates iOS devices reachable over USB (via libimobiledevice / usbmuxd). Kept separate from
/// <see cref="IIPhoneFileSystemService"/> so the browser dialog can list devices without opening an
/// AFC session. Implementations must never throw: a missing native library, absent usbmuxd, or an
/// unpaired device yields an empty list rather than an exception, so the UI degrades gracefully.
/// </summary>
public interface IIPhoneDeviceService
{
    /// <summary>Returns the currently connected, paired iOS devices. Best-effort; empty on failure.</summary>
    IReadOnlyList<IPhoneDeviceInfo> GetDevices();
}
