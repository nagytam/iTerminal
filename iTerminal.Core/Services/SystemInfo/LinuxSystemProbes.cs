namespace iTerminal.Core.Services.SystemInfo;

/// <summary>
/// Linux reads: <c>/proc</c> for the processor, memory and disks, <c>/sys</c> for graphics cards and batteries. No
/// program is started, so a read that hangs is a read of a file that never returns and the probe's budget bounds it.
/// </summary>
public sealed class LinuxSystemProbes : SystemProbesBase
{
    private const string PciIdsPath = "/usr/share/misc/pci.ids";
    private const string AlternatePciIdsPath = "/usr/share/hwdata/pci.ids";
    private const string SysBlock = "/sys/block";
    private const string SysDrm = "/sys/class/drm";
    private const string SysPowerSupply = "/sys/class/power_supply";

    public override string CpuModel() =>
        SystemInfoParsers.ParseCpuModel(File.ReadAllText("/proc/cpuinfo"))
        ?? throw new InvalidOperationException("The processor model is not in /proc/cpuinfo.");

    public override CpuTicks ReadCpuTicks() =>
        SystemInfoParsers.ParseCpuTicks(File.ReadAllText("/proc/stat"))
        ?? throw new InvalidOperationException("The processor counters are not in /proc/stat.");

    public override MemoryReading ReadMemory() =>
        SystemInfoParsers.ParseMemInfo(File.ReadAllText("/proc/meminfo"))
        ?? throw new InvalidOperationException("The memory figures are not in /proc/meminfo.");

    public override IReadOnlyList<GpuReading> ReadGpus()
    {
        var result = new List<GpuReading>();
        if (!Directory.Exists(SysDrm))
        {
            return result;
        }

        string? pciIds = ReadFirstExisting(PciIdsPath, AlternatePciIdsPath);
        foreach (string card in Directory.GetDirectories(SysDrm).Where(IsCardDirectory).OrderBy(path => path, StringComparer.Ordinal))
        {
            string device = Path.Combine(card, "device");
            string? ueventText = TryRead(Path.Combine(device, "uevent"));
            var pci = ueventText is null ? null : SystemInfoParsers.ParsePciId(ueventText);
            string name;
            if (pci is { } ids && pciIds is not null) name = SystemInfoParsers.ParsePciName(pciIds, ids.Vendor, ids.Device) ?? $"{ids.Vendor}:{ids.Device}";
            else if (pci is { } raw) name = $"{raw.Vendor}:{raw.Device}";
            else name = Path.GetFileName(card);

            string? driver = Directory.Exists(device) && new DirectoryInfo(Path.Combine(device, "driver")).LinkTarget is { } link
                ? Path.GetFileName(link)
                : null;
            long? vram = long.TryParse(TryRead(Path.Combine(device, "mem_info_vram_total"))?.Trim(), out long bytes) ? bytes : null;
            result.Add(new GpuReading(name, driver, vram));
        }

        return result;
    }

    public override double? ReadGpuLoadPercent()
    {
        if (!Directory.Exists(SysDrm))
        {
            return null;
        }

        foreach (string card in Directory.GetDirectories(SysDrm).Where(IsCardDirectory).OrderBy(path => path, StringComparer.Ordinal))
        {
            if (int.TryParse(TryRead(Path.Combine(card, "device", "gpu_busy_percent"))?.Trim(), out int busy))
            {
                return busy;
            }
        }

        return null;
    }

    public override IReadOnlyList<string> BatteryNames() =>
        Directory.Exists(SysPowerSupply)
            ? Directory.GetDirectories(SysPowerSupply).Select(Path.GetFileName).Where(name => name is not null && name.StartsWith("BAT", StringComparison.Ordinal)).Select(name => name!).OrderBy(name => name, StringComparer.Ordinal).ToList()
            : [];

    public override BatteryReading? ReadBattery(string name)
    {
        string directory = Path.Combine(SysPowerSupply, name);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(directory))
        {
            string? text = TryRead(file);
            if (text is not null)
            {
                attributes[Path.GetFileName(file)] = text;
            }
        }

        return SystemInfoParsers.ParsePowerSupply(attributes);
    }

    public override IReadOnlyList<string> DiskNames()
    {
        if (!File.Exists("/proc/diskstats"))
        {
            return [];
        }

        return SystemInfoParsers.ParseDiskStats(File.ReadAllText("/proc/diskstats")).Keys
            .Where(name => !SystemInfoParsers.IsExcludedDiskName(name) && Directory.Exists(Path.Combine(SysBlock, name)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    public override DiskCounters ReadDisk(string name) =>
        SystemInfoParsers.ParseDiskStats(File.ReadAllText("/proc/diskstats")).TryGetValue(name, out DiskCounters? counters)
            ? counters
            : throw new InvalidOperationException($"{name} is not in /proc/diskstats.");

    private static bool IsCardDirectory(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith("card", StringComparison.Ordinal)
            && name.Length > 4
            && name[4..].All(char.IsAsciiDigit);
    }

    private static string? ReadFirstExisting(params string[] paths)
    {
        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                return TryRead(path);
            }
        }

        return null;
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
