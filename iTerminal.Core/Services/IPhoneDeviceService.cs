using iMobileDevice;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;
using iTerminal.Core.Data;

namespace iTerminal.Core.Services;

/// <summary>
/// <see cref="IIPhoneDeviceService"/> backed by libimobiledevice through imobiledevice-net. Device
/// enumeration goes through usbmuxd; the device name comes from a short-lived lockdown handshake.
/// Every native call is guarded — no device, no driver, or an unpaired phone simply produces an
/// empty result, matching the "never throw" contract of the interface.
/// </summary>
public sealed class IPhoneDeviceService : IIPhoneDeviceService
{
    public IReadOnlyList<IPhoneDeviceInfo> GetDevices()
    {
        try
        {
            var idevice = LibiMobileDevice.Instance.iDevice;
            var lockdown = LibiMobileDevice.Instance.Lockdown;

            int count = 0;
            if (idevice.idevice_get_device_list(out var udids, ref count) != iDeviceError.Success
                || udids is null)
            {
                return [];
            }

            var devices = new List<IPhoneDeviceInfo>(udids.Count);
            foreach (var udid in udids.Distinct())
            {
                devices.Add(DescribeDevice(idevice, lockdown, udid));
            }
            return devices;
        }
        catch
        {
            // Native library missing / usbmuxd unavailable — surface no devices rather than crash.
            return [];
        }
    }

    private static IPhoneDeviceInfo DescribeDevice(IiDeviceApi idevice, ILockdownApi lockdown, string udid)
    {
        iDeviceHandle? deviceHandle = null;
        LockdownClientHandle? lockdownHandle = null;
        try
        {
            if (idevice.idevice_new(out deviceHandle, udid) != iDeviceError.Success || deviceHandle is null)
            {
                return new IPhoneDeviceInfo(udid, udid, string.Empty);
            }

            if (lockdown.lockdownd_client_new_with_handshake(deviceHandle, out lockdownHandle, "iTerminal")
                    != LockdownError.Success
                || lockdownHandle is null)
            {
                return new IPhoneDeviceInfo(udid, udid, string.Empty);
            }

            var name = lockdown.lockdownd_get_device_name(lockdownHandle, out var deviceName) == LockdownError.Success
                && !string.IsNullOrEmpty(deviceName)
                ? deviceName
                : udid;

            return new IPhoneDeviceInfo(udid, name, string.Empty);
        }
        catch
        {
            return new IPhoneDeviceInfo(udid, udid, string.Empty);
        }
        finally
        {
            lockdownHandle?.Dispose();
            deviceHandle?.Dispose();
        }
    }
}
