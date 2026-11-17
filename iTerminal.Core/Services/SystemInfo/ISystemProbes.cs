using System.Collections;
using System.Net.NetworkInformation;

namespace iTerminal.Core.Services.SystemInfo;

/// <summary>
/// The reads the system information report is built from. Each method reads one value and throws when it cannot, so a
/// probe unit reports the failure as <see cref="ProbeState.NotReported"/>. The calls are read-only.
/// </summary>
public interface ISystemProbes
{
    string OperatingSystem();

    string Architecture();

    string Framework();

    string UserName();

    string UserDomain();

    string HomeFolder();

    IReadOnlyList<KeyValuePair<string, string>> EnvironmentVariables();

    string CpuModel();

    int LogicalProcessors();

    CpuTicks ReadCpuTicks();

    MemoryReading ReadMemory();

    IReadOnlyList<GpuReading> ReadGpus();

    double? ReadGpuLoadPercent();

    IReadOnlyList<string> DriveNames();

    DriveReading ReadDrive(string name);

    IReadOnlyList<InterfaceReading> ReadInterfaces();

    IReadOnlyList<string> BatteryNames();

    BatteryReading? ReadBattery(string name);

    IReadOnlyList<string> DiskNames();

    DiskCounters ReadDisk(string name);

    IReadOnlyList<string> NetworkNames();

    NetworkCounters ReadNetwork(string name);
}

/// <summary>Chooses the probes for the operating system the application runs on.</summary>
public interface ISystemProbeFactory
{
    ISystemProbes Create();
}

public sealed class SystemProbeFactory : ISystemProbeFactory
{
    public ISystemProbes Create() => OperatingSystem.IsWindows() ? new WindowsSystemProbes() : new LinuxSystemProbes();
}

/// <summary>The reads that work the same on every platform: identity, drives, network interfaces and their counters.</summary>
public abstract class SystemProbesBase : ISystemProbes
{
    public string OperatingSystem() => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    public string Architecture() => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();

    public string Framework() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    public string UserName() => Environment.UserName;

    public string UserDomain() => Environment.UserDomainName;

    public string HomeFolder() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public IReadOnlyList<KeyValuePair<string, string>> EnvironmentVariables()
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
        {
            result.Add(new KeyValuePair<string, string>(entry.Key.ToString() ?? string.Empty, entry.Value?.ToString() ?? string.Empty));
        }

        result.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public int LogicalProcessors() => Environment.ProcessorCount;

    public abstract string CpuModel();

    public abstract CpuTicks ReadCpuTicks();

    public abstract MemoryReading ReadMemory();

    public abstract IReadOnlyList<GpuReading> ReadGpus();

    public abstract double? ReadGpuLoadPercent();

    public abstract IReadOnlyList<string> BatteryNames();

    public abstract BatteryReading? ReadBattery(string name);

    public abstract IReadOnlyList<string> DiskNames();

    public abstract DiskCounters ReadDisk(string name);

    public IReadOnlyList<string> DriveNames() => DriveInfo.GetDrives().Select(drive => drive.Name).ToList();

    public DriveReading ReadDrive(string name)
    {
        var drive = new DriveInfo(name);
        if (!drive.IsReady)
        {
            return new DriveReading(name, drive.DriveType.ToString(), string.Empty, string.Empty, false, 0, 0);
        }

        return new DriveReading(name, drive.DriveType.ToString(), drive.DriveFormat, drive.VolumeLabel, true, drive.TotalSize, drive.TotalFreeSpace);
    }

    public IReadOnlyList<InterfaceReading> ReadInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Select(nic => new InterfaceReading(
                nic.Name,
                nic.NetworkInterfaceType.ToString(),
                nic.OperationalStatus.ToString(),
                nic.GetIPProperties().UnicastAddresses.Select(address => address.Address.ToString()).ToList()))
            .ToList();

    public IReadOnlyList<string> NetworkNames() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(nic => nic.Name)
            .ToList();

    public NetworkCounters ReadNetwork(string name)
    {
        NetworkInterface nic = NetworkInterface.GetAllNetworkInterfaces().First(candidate => candidate.Name == name);
        var statistics = nic.GetIPv4Statistics();
        return new NetworkCounters((ulong)statistics.BytesReceived, (ulong)statistics.BytesSent);
    }
}
