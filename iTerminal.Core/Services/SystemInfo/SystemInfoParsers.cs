using System.Globalization;

namespace iTerminal.Core.Services.SystemInfo;

/// <summary>
/// Reads what the operating system's files and calls say, without touching the machine. Everything here is a pure
/// function of its input, so the formats are tested against fixture text on any platform.
/// </summary>
public static class SystemInfoParsers
{
    private const ulong SectorBytes = 512;

    /// <summary>The model name of the first processor in a <c>/proc/cpuinfo</c> text; null when it has none (ARM).</summary>
    public static string? ParseCpuModel(string cpuinfo)
    {
        var modelLine = cpuinfo.Split('\n').FirstOrDefault(candidate => candidate.StartsWith("model name", StringComparison.Ordinal));
        if (modelLine is null) return null;

        int colon = modelLine.IndexOf(':');
        string model = colon < 0 ? string.Empty : modelLine[(colon + 1)..].Trim();
        return model.Length == 0 ? null : model;
    }

    /// <summary>The aggregate <c>cpu</c> line of <c>/proc/stat</c>: idle time (including I/O wait) and all time, in jiffies.</summary>
    public static CpuTicks? ParseCpuTicks(string stat)
    {
        foreach (string line in stat.Split('\n'))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 6)
            {
                return null;
            }

            // user, nice, system, idle, iowait, irq, softirq, steal. Guest time is already counted in user and nice,
            // so guest and guest_nice are left out of the total.
            ulong[] values = new ulong[Math.Min(fields.Length - 1, 8)];
            for (int i = 1; i <= values.Length; i++)
            {
                if (!ulong.TryParse(fields[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i - 1]))
                {
                    return null;
                }
            }

            ulong idle = values[3] + values[4];
            ulong total = values.Aggregate(0UL, (sum, value) => sum + value);
            return new CpuTicks(idle, total);
        }

        return null;
    }

    /// <summary>The total and available memory of a <c>/proc/meminfo</c> text, in bytes; null unless both are present.</summary>
    public static MemoryReading? ParseMemInfo(string meminfo)
    {
        ulong? total = null;
        ulong? available = null;
        foreach (string line in meminfo.Split('\n'))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                total = ParseKilobytes(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                available = ParseKilobytes(line);
            }
        }

        return total is { } t && available is { } a ? new MemoryReading(t, a) : null;
    }

    /// <summary>The cumulative sectors read and written per device in a <c>/proc/diskstats</c> text, in bytes.</summary>
    public static IReadOnlyDictionary<string, DiskCounters> ParseDiskStats(string diskstats)
    {
        var result = new Dictionary<string, DiskCounters>(StringComparer.Ordinal);
        foreach (string line in diskstats.Split('\n'))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10
                || !ulong.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out ulong read)
                || !ulong.TryParse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture, out ulong written))
            {
                continue;
            }

            result[fields[2]] = new DiskCounters(read * SectorBytes, written * SectorBytes);
        }

        return result;
    }

    /// <summary>True for devices that are not disks in a disk-activity table: loop, RAM, compressed RAM and optical drives.</summary>
    public static bool IsExcludedDiskName(string name) =>
        name.StartsWith("loop", StringComparison.Ordinal)
        || name.StartsWith("ram", StringComparison.Ordinal)
        || name.StartsWith("zram", StringComparison.Ordinal)
        || name.StartsWith("sr", StringComparison.Ordinal);

    /// <summary>The rate between two reads of one cumulative counter; null when no time has passed or the counter went back.</summary>
    public static double? PerSecond(ulong previous, ulong current, long previousMilliseconds, long currentMilliseconds)
    {
        long elapsed = currentMilliseconds - previousMilliseconds;
        if (elapsed <= 0 || current < previous)
        {
            return null;
        }

        return (current - previous) * 1000.0 / elapsed;
    }

    /// <summary>The processor load over the time between two <c>/proc/stat</c> reads, in percent; null when nothing changed or the counters went back.</summary>
    public static double? CpuLoadPercent(CpuTicks previous, CpuTicks current)
    {
        if (current.Total < previous.Total || current.Idle < previous.Idle)
        {
            return null;
        }

        ulong total = current.Total - previous.Total;
        if (total == 0)
        {
            return null;
        }

        double idle = current.Idle - previous.Idle;
        return Math.Clamp((1 - idle / total) * 100, 0, 100);
    }

    /// <summary>The vendor and device ids of a PCI device from its <c>uevent</c> text (<c>PCI_ID=10DE:1B80</c>).</summary>
    public static (string Vendor, string Device)? ParsePciId(string uevent)
    {
        foreach (string line in uevent.Split('\n'))
        {
            if (!line.StartsWith("PCI_ID=", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line["PCI_ID=".Length..].Trim().Split(':');
            return parts.Length == 2 ? (parts[0].ToLowerInvariant(), parts[1].ToLowerInvariant()) : null;
        }

        return null;
    }

    /// <summary>The device's name from a <c>pci.ids</c> text; null when the vendor or device is not listed.</summary>
    public static string? ParsePciName(string pciIds, string vendorId, string deviceId)
    {
        bool inVendor = false;
        foreach (string line in pciIds.Split('\n'))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                inVendor = line.StartsWith(vendorId + "  ", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inVendor && line.StartsWith('\t') && !line.StartsWith("\t\t", StringComparison.Ordinal)
                && line[1..].StartsWith(deviceId + "  ", StringComparison.OrdinalIgnoreCase))
            {
                return line[(1 + deviceId.Length + 2)..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// The battery described by a <c>/sys/class/power_supply/BAT*</c> directory, from its attribute files. Energy values
    /// are in µWh and µW, charge values in µAh and µA; a kernel that gives charge gets the time from the current.
    /// </summary>
    public static BatteryReading ParsePowerSupply(IReadOnlyDictionary<string, string> attributes)
    {
        double? Number(string key) =>
            attributes.TryGetValue(key, out string? text)
            && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : null;

        BatteryStatus status = attributes.GetValueOrDefault("status")?.Trim() switch
        {
            "Charging" => BatteryStatus.Charging,
            "Discharging" => BatteryStatus.Discharging,
            "Full" => BatteryStatus.Full,
            "Not charging" => BatteryStatus.NotCharging,
            _ => BatteryStatus.Unknown,
        };

        double? percent = Number("capacity");
        double? power = Number("power_now");
        double? current = Number("current_now");
        if (power is null && current is { } amps && Number("voltage_now") is { } volts)
        {
            power = amps * volts / 1_000_000;
        }

        double? energyNow = Number("energy_now");
        double? energyFull = Number("energy_full");
        double? energyDesign = Number("energy_full_design");
        double? chargeNow = Number("charge_now");

        TimeSpan? remaining = null;
        TimeSpan? toFull = null;
        if (Number("time_to_empty_now") is { } secondsToEmpty && secondsToEmpty > 0)
        {
            remaining = TimeSpan.FromSeconds(secondsToEmpty);
        }
        else if (energyNow is { } now && power is > 0)
        {
            // µWh divided by µW is hours.
            remaining = TimeSpan.FromHours(now / power.Value);
        }
        else if (chargeNow is { } charge && current is > 0)
        {
            // µAh divided by µA is hours.
            remaining = TimeSpan.FromHours(charge / current.Value);
        }

        if (Number("time_to_full_now") is { } secondsToFull && secondsToFull > 0)
        {
            toFull = TimeSpan.FromSeconds(secondsToFull);
        }
        else if (status == BatteryStatus.Charging && energyNow is { } nowE && energyFull is { } fullE && power is > 0 && fullE > nowE)
        {
            toFull = TimeSpan.FromHours((fullE - nowE) / power.Value);
        }

        double? designWh = energyDesign is { } design ? design / 1_000_000 : null;
        double? fullWh = energyFull is { } full ? full / 1_000_000 : null;
        int? cycles = Number("cycle_count") is { } cycleValue ? (int)cycleValue : null;

        return new BatteryReading(
            status,
            percent,
            remaining,
            toFull,
            attributes.GetValueOrDefault("manufacturer")?.Trim(),
            attributes.GetValueOrDefault("model_name")?.Trim(),
            attributes.GetValueOrDefault("technology")?.Trim(),
            designWh,
            fullWh,
            cycles,
            power is { } watts ? watts / 1_000_000 : null);
    }

    /// <summary>
    /// The battery from <c>GetSystemPowerStatus</c>. A flag of 128 means there is no system battery; 255 or a negative
    /// time means the value is unknown. The API gives neither capacity, so health is never reported from it.
    /// </summary>
    public static BatteryReading? MapPowerStatus(byte acLineStatus, byte batteryLifePercent, byte batteryFlag, int batteryLifeSeconds)
    {
        const byte NoBattery = 128;
        const byte Unknown = 255;
        const byte Charging = 8;

        if (batteryFlag == NoBattery)
        {
            return null;
        }

        BatteryStatus status;
        if (batteryFlag != Unknown && (batteryFlag & Charging) != 0)
        {
            status = BatteryStatus.Charging;
        }
        else if (acLineStatus == 0)
        {
            status = BatteryStatus.Discharging;
        }
        else if (acLineStatus == 1)
        {
            status = batteryLifePercent == 100 ? BatteryStatus.Full : BatteryStatus.NotCharging;
        }
        else
        {
            status = BatteryStatus.Unknown;
        }

        double? percent = batteryLifePercent == Unknown ? null : batteryLifePercent;
        TimeSpan? remaining = batteryLifeSeconds < 0 ? null : TimeSpan.FromSeconds(batteryLifeSeconds);
        return new BatteryReading(status, percent, remaining, null, null, null, null, null, null, null, null);
    }

    private static ulong? ParseKilobytes(string line)
    {
        string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && ulong.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong kilobytes)
            ? kilobytes * 1024
            : null;
    }
}
