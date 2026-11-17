namespace iTerminal.Core.Services.SystemInfo;

/// <summary>
/// What a probe reports. The sampler only says which state a value is in; the dialog turns every state into words
/// through the localizer, so no English text is ever stored in a snapshot.
/// </summary>
public enum ProbeState
{
    /// <summary>The value was read.</summary>
    Ok,

    /// <summary>The machine does not offer this value (no sysfs entry, no driver, no battery), or the read failed.</summary>
    NotReported,

    /// <summary>The read did not finish within its time budget. Its late result, if any, is discarded.</summary>
    NotAnswering,

    /// <summary>A rate needs a second sample before it can be shown.</summary>
    Measuring,
}

public enum BatteryStatus
{
    Unknown,
    Charging,
    Discharging,
    Full,
    NotCharging,
}

/// <summary>A value with the state it was read in.</summary>
public readonly record struct Probed<T>(ProbeState State, T? Value)
{
    public static Probed<T> Ok(T value) => new(ProbeState.Ok, value);

    public static Probed<T> NotReported => new(ProbeState.NotReported, default);

    public static Probed<T> NotAnswering => new(ProbeState.NotAnswering, default);

    public static Probed<T> Measuring => new(ProbeState.Measuring, default);
}

/// <summary>A value for one named item (a drive, a disk, a network interface, a battery).</summary>
public sealed record NamedProbe<T>(string Name, Probed<T> Value);

/// <summary>A counter read together with the monotonic time it was read at, so a rate is taken between two reads that were measured.</summary>
public sealed record Timed<T>(T Value, long Milliseconds);

public sealed record CpuTicks(ulong Idle, ulong Total);

public sealed record MemoryReading(ulong TotalBytes, ulong AvailableBytes);

public sealed record DiskCounters(ulong ReadBytes, ulong WriteBytes);

public sealed record NetworkCounters(ulong ReceivedBytes, ulong SentBytes);

public sealed record DriveReading(string Name, string DriveType, string Format, string Label, bool IsReady, long TotalBytes, long FreeBytes);

public sealed record InterfaceReading(string Name, string Type, string Status, IReadOnlyList<string> Addresses);

public sealed record GpuReading(string Name, string? Driver, long? VideoMemoryBytes);

public sealed record BatteryReading(
    BatteryStatus Status,
    double? Percent,
    TimeSpan? TimeRemaining,
    TimeSpan? TimeToFull,
    string? Manufacturer,
    string? Model,
    string? Technology,
    double? DesignWattHours,
    double? FullWattHours,
    int? Cycles,
    double? PowerWatts);

public sealed record DiskRate(double ReadBytesPerSecond, double WriteBytesPerSecond);

public sealed record NetworkRate(double ReceivedBytesPerSecond, double SentBytesPerSecond);

/// <summary>
/// Everything the dialog shows, as one immutable value. Each field is one probe unit's result. The lists of names come
/// from a listing unit; each item's value comes from its own unit.
/// </summary>
public sealed record SystemSnapshot(
    Probed<string> OperatingSystem,
    Probed<string> Architecture,
    Probed<string> Framework,
    Probed<string> UserName,
    Probed<string> UserDomain,
    Probed<string> HomeFolder,
    Probed<IReadOnlyList<KeyValuePair<string, string>>> EnvironmentVariables,
    Probed<string> CpuModel,
    Probed<int> LogicalProcessors,
    Probed<double> CpuLoadPercent,
    Probed<ulong> PhysicalMemoryTotal,
    Probed<ulong> PhysicalMemoryAvailable,
    Probed<IReadOnlyList<GpuReading>> Gpus,
    Probed<double> GpuLoadPercent,
    Probed<IReadOnlyList<string>> DriveNames,
    IReadOnlyList<NamedProbe<DriveReading>> Drives,
    Probed<IReadOnlyList<InterfaceReading>> Interfaces,
    Probed<IReadOnlyList<string>> BatteryNames,
    IReadOnlyList<NamedProbe<BatteryReading>> Batteries,
    Probed<IReadOnlyList<string>> DiskNames,
    IReadOnlyList<NamedProbe<DiskRate>> Disks,
    Probed<IReadOnlyList<string>> NetworkNames,
    IReadOnlyList<NamedProbe<NetworkRate>> Network);
