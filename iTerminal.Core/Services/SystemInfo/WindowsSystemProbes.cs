using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace iTerminal.Core.Services.SystemInfo;

/// <summary>
/// Windows reads: the registry for the processor and graphics identity, and the kernel32 and pdh calls for the rest.
/// The disk counters use the same control code that Task Manager reads, opened with no access rights, so no
/// elevation is needed. The PDH GPU counter is added by its English name, so it works on a localized Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSystemProbes : SystemProbesBase
{
    private const uint IoctlDiskPerformance = 0x00070020;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;
    private const int PdhItemSize = 24;
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string GpuUtilizationPath = @"\GPU Engine(*)\Utilization Percentage";

    private IntPtr _gpuQuery;
    private IntPtr _gpuCounter;

    public override string CpuModel() =>
        Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?.GetValue("ProcessorNameString") as string
        ?? throw new InvalidOperationException("The processor name is not in the registry.");

    public override CpuTicks ReadCpuTicks()
    {
        if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user))
        {
            throw new InvalidOperationException("GetSystemTimes failed.");
        }

        // Kernel time includes idle time, so the total is kernel plus user.
        return new CpuTicks(ToUlong(idle), ToUlong(kernel) + ToUlong(user));
    }

    public override MemoryReading ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException("GlobalMemoryStatusEx failed.");
        }

        return new MemoryReading(status.TotalPhysical, status.AvailablePhysical);
    }

    public override IReadOnlyList<GpuReading> ReadGpus()
    {
        var result = new List<GpuReading>();
        using RegistryKey? cls = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (cls is null)
        {
            return result;
        }

        foreach (string subKey in cls.GetSubKeyNames().Where(name => Regex.IsMatch(name, "^[0-9]{4}$", RegexOptions.None, TimeSpan.FromSeconds(1))).OrderBy(name => name, StringComparer.Ordinal))
        {
            using RegistryKey? card = cls.OpenSubKey(subKey);
            if (card?.GetValue("DriverDesc") is not string name)
            {
                continue;
            }

            long? memory = card.GetValue("HardwareInformation.qwMemorySize") switch
            {
                long bytes => bytes,
                byte[] raw when raw.Length >= 8 => BitConverter.ToInt64(raw, 0),
                _ => null,
            };
            result.Add(new GpuReading(name, card.GetValue("DriverVersion") as string, memory));
        }

        return result;
    }

    public override double? ReadGpuLoadPercent()
    {
        if (_gpuQuery == IntPtr.Zero && PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _gpuQuery) != 0)
        {
            return null;
        }

        if (_gpuCounter == IntPtr.Zero && PdhAddEnglishCounterW(_gpuQuery, GpuUtilizationPath, IntPtr.Zero, out _gpuCounter) != 0)
        {
            return null;
        }

        // A rate needs two collections: the first pass after the query opens has nothing to report.
        if (PdhCollectQueryData(_gpuQuery) != 0)
        {
            return null;
        }

        uint size = 0;
        uint count = 0;
        if (PdhGetFormattedCounterArrayW(_gpuCounter, PdhFmtDouble, ref size, ref count, IntPtr.Zero) != PdhMoreData || count == 0)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(_gpuCounter, PdhFmtDouble, ref size, ref count, buffer) != 0)
            {
                return null;
            }

            // Each process has one instance per engine, and an engine type (3D, copy, video decode...) can run on several.
            // The figure is the busiest engine type: the sum over processes for each type, then the largest of those, not
            // the sum over every engine. Across several adapters this is the busiest of all of them, one figure for the
            // machine; Task Manager shows adapters separately.
            var byEngineType = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                int offset = i * PdhItemSize;
                uint status = (uint)Marshal.ReadInt32(buffer, offset + IntPtr.Size);
                if (status is not (0 or 1))
                {
                    continue;
                }

                string instance = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, offset)) ?? string.Empty;
                string key = EngineType(instance);
                byEngineType[key] = byEngineType.GetValueOrDefault(key) + BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buffer, offset + 16));
            }

            // No engine answered with valid data: that is not a load of zero, so it is reported as not reported.
            return byEngineType.Count == 0 ? null : Math.Min(100, byEngineType.Values.Max());
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public override IReadOnlyList<string> BatteryNames() =>
        ReadPowerStatus(out _) ? ["Battery"] : [];

    public override BatteryReading? ReadBattery(string name)
    {
        if (!ReadPowerStatus(out SystemPowerStatus status))
        {
            return null;
        }

        return SystemInfoParsers.MapPowerStatus(status.ACLineStatus, status.BatteryLifePercent, status.BatteryFlag, (int)status.BatteryLifeTime);
    }

    /// <summary>The physical drives are scanned once every <see cref="DiskScanEvery"/> passes, not on each one: opening up to 32 handles a second is noise, and drives change rarely.</summary>
    public override IReadOnlyList<string> DiskNames()
    {
        lock (_diskNameLock)
        {
            if (_diskNames is null || ++_diskNameReads >= DiskScanEvery)
            {
                _diskNameReads = 0;
                _diskNames = ScanDiskNames();
            }

            return _diskNames;
        }
    }

    private readonly Lock _diskNameLock = new();
    private IReadOnlyList<string>? _diskNames;
    private int _diskNameReads;
    private const int DiskScanEvery = 30;

    private static IReadOnlyList<string> ScanDiskNames()
    {
        var result = new List<string>();
        for (int number = 0; number < 32; number++)
        {
            string name = $"PhysicalDrive{number}";
            try
            {
                using SafeFileHandle handle = OpenDevice(name);
                result.Add(name);
            }
            catch (IOException)
            {
                // No drive with this number.
            }
            catch (UnauthorizedAccessException)
            {
                // A drive that exists but the account may not query.
            }
        }

        return result;
    }

    public override DiskCounters ReadDisk(string name)
    {
        using SafeFileHandle handle = OpenDevice(name);
        byte[] output = new byte[256];
        if (!DeviceIoControl(handle, IoctlDiskPerformance, IntPtr.Zero, 0, output, (uint)output.Length, out uint returned, IntPtr.Zero)
            || returned < 16)
        {
            throw new InvalidOperationException($"{name} did not report its counters.");
        }

        return new DiskCounters(BitConverter.ToUInt64(output, 0), BitConverter.ToUInt64(output, 8));
    }

    /// <summary>Opens a device with no access rights, which is enough to read its counters and needs no elevation.</summary>
    private static SafeFileHandle OpenDevice(string name)
    {
        const uint NoAccess = 0;
        const uint ShareReadWriteDelete = 0x1 | 0x2 | 0x4;
        const uint OpenExisting = 3;
        SafeFileHandle handle = CreateFileW(@"\\.\" + name, NoAccess, ShareReadWriteDelete, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new IOException($"{name} cannot be opened.");
        }

        return handle;
    }

    private static bool ReadPowerStatus(out SystemPowerStatus status) => GetSystemPowerStatus(out status) && status.BatteryFlag != 128;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    private static ulong ToUlong(FILETIME time) => ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;

    /// <summary>
    /// The adapter and engine type of a GPU Engine instance name, such as
    /// <c>pid_1234_luid_0x0_0x1234_phys_0_eng_0_engtype_3D</c>. The process is left out, so each engine type is summed
    /// over the processes that use it.
    /// </summary>
    private static string EngineType(string instance)
    {
        int luid = instance.IndexOf("luid_", StringComparison.Ordinal);
        int engine = instance.IndexOf("_eng_", StringComparison.Ordinal);
        string adapter = luid >= 0 && engine > luid ? instance[luid..engine] : string.Empty;
        int type = instance.IndexOf("engtype_", StringComparison.Ordinal);
        string engineType = type >= 0 ? instance[(type + "engtype_".Length)..] : string.Empty;
        return adapter + "|" + engineType;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr input, uint inputSize, byte[] output, uint outputSize, out uint returned, IntPtr overlapped);

    [DllImport("pdh.dll")]
    private static extern uint PdhOpenQuery(IntPtr dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    #pragma warning disable S101 // The name is the product's own naming (iTerminal, iPhone) or the Win32 name the structure mirrors; renaming it would hide the link.
    private struct FILETIME
    #pragma warning restore S101
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
