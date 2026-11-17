using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using iConsole.Controls.Charts;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Dialogs;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services.SystemInfo;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The system information report (Ctrl+F11). The dialog probes nothing: it watches the sampler while it is open, reads
/// the snapshot the sampler publishes, and formats it. Every word is looked up through the localizer, so a language
/// change re-formats the current snapshot without touching the machine again.
/// </summary>
public class SystemInfoDialog : Dialog
{
    private const int HistoryLength = 120;
    private const int LabelWidth = 18;

    private readonly TextViewerControl _textViewerControl;
    private readonly Gauge _memoryGauge;
    private readonly LineChart _memoryTrend;
    private readonly List<double> _memoryHistory = [];
    private readonly ISystemSampler _sampler;
    private readonly ILocalizer _localizer;
    private IDisposable? _watch;
    private SystemSnapshot? _shown;
    private bool _shownFailing;
    private volatile bool _languageChanged;
    private bool _closed;

    public SystemInfoDialog(IThemeService themeService, ISystemSampler sampler, ILocalizer localizer)
        : base(nameof(SystemInfoDialog), themeService)
    {
        _sampler = sampler;
        _localizer = localizer;
        Title = UiText.Get("Dialog.SystemInformation", "System Information");
        _textViewerControl = new TextViewerControl(false, themeService);
        AddControl(_textViewerControl);

        // A live strip under the report: how much of the machine's physical memory is in use, and the trend.
        _memoryGauge = new Gauge(themeService) { Label = UiText.Get("SysInfo.Label.MemoryUsed", "Memory used"), ViewSize = new Size(1, 1) };
        _memoryTrend = new LineChart(themeService) { Foreground = Color.Cyan, ViewSize = new Size(1, 2) };
        AddControl(_memoryGauge);
        AddControl(_memoryTrend);
    }

    /// <summary>The memory use the strip has been fed, one value per snapshot, oldest first.</summary>
    internal IReadOnlyList<double> MemoryHistoryForTest => _memoryHistory;

    /// <summary>The lines the report shows now.</summary>
    internal IReadOnlyList<string> LinesForTest => [.. _textViewerControl.TextModel.Lines];

    /// <summary>The English placeholder, the fallback when no language has the key. Tests compare with it.</summary>
    internal const string CollectingFallback = "Collecting system information…";

    internal const string CollectingText = "  " + CollectingFallback;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _textViewerControl.TextModel.Lines = [.. CollectingLines(false)];
        _watch = _sampler.Watch();
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        _textViewerControl.Position = new Position(1, 1);
        const int stripHeight = 3;
        _textViewerControl.ViewSize = new Size(ViewSize.Width - 2, Math.Max(1, ViewSize.Height - 2 - stripHeight));
        long stripY = ViewSize.Height - 1 - stripHeight;
        _memoryGauge.Position = new Position(2, stripY);
        _memoryGauge.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), 1);
        _memoryTrend.Position = new Position(2, stripY + 1);
        _memoryTrend.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), 2);

        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();

        SystemSnapshot? snapshot = _sampler.Current;
        bool languageChanged = _languageChanged;
        _languageChanged = false;
        bool failing = _sampler.PassFailing;
        if (snapshot is null)
        {
            // No pass has succeeded yet: the placeholder stays, with the failure notice if the passes are failing.
            if (languageChanged || failing != _shownFailing)
            {
                _shownFailing = failing;
                _textViewerControl.TextModel.Lines = [.. CollectingLines(failing)];
            }

            return;
        }

        if (!ReferenceEquals(snapshot, _shown) || languageChanged || failing != _shownFailing)
        {
            bool newSnapshot = !ReferenceEquals(snapshot, _shown);
            _shown = snapshot;
            _shownFailing = failing;
            _textViewerControl.TextModel.Lines = [.. BuildReport(snapshot, failing).Split(Environment.NewLine)];
            if (newSnapshot)
            {
                FeedMemoryStrip(snapshot);
            }
        }
    }

    public override void Close()
    {
        base.Close();

        if (!_closed)
        {
            _closed = true;
            _localizer.LanguageChanged -= OnLanguageChanged;
            _watch?.Dispose();
            _watch = null;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => _languageChanged = true;

    private void FeedMemoryStrip(SystemSnapshot snapshot)
    {
        if (snapshot.PhysicalMemoryTotal.State != ProbeState.Ok || snapshot.PhysicalMemoryAvailable.State != ProbeState.Ok)
        {
            return;
        }

        ulong total = snapshot.PhysicalMemoryTotal.Value;
        if (total == 0)
        {
            return;
        }

        double used = Math.Clamp((double)(total - snapshot.PhysicalMemoryAvailable.Value) / total, 0, 1);
        _memoryHistory.Add(used);
        if (_memoryHistory.Count > HistoryLength)
        {
            _memoryHistory.RemoveAt(0);
        }

        _memoryGauge.Value = used;
        _memoryTrend.Values = _memoryHistory.ToArray();
    }

    private string Text(string key, string fallback) =>
        _localizer.TryGet(key, out string value) && value.Length > 0 ? value : fallback;

    private string Format(string key, string fallback, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Text(key, fallback), args);

    /// <summary>The placeholder while no pass has succeeded, with the failure notice when the passes are failing.</summary>
    private IEnumerable<string> CollectingLines(bool failing)
    {
        yield return "  " + Text("SysInfo.Collecting", CollectingFallback);
        if (failing)
        {
            yield return string.Empty;
            yield return "  " + StaleText();
        }
    }

    private string StaleText() => Text("SysInfo.Stale", "The last sample failed; the values shown may be out of date.");

    private string Label(string key, string fallback) => Text(key, fallback).PadRight(LabelWidth);

    private string Header(string key, string fallback) => $"══ {Text(key, fallback)} ".PadRight(64, '═');

    private string StateWord(ProbeState state) => state switch
    {
        ProbeState.NotAnswering => Text("SysInfo.State.NotAnswering", "not answering"),
        ProbeState.Measuring => Text("SysInfo.State.Measuring", "measuring"),
        _ => Text("SysInfo.State.NotReported", "not reported"),
    };

    private string Value<T>(Probed<T> probed, Func<T, string> format) =>
        probed.State == ProbeState.Ok && probed.Value is { } value ? format(value) : StateWord(probed.State);

    private static string Gigabytes(double bytes) => (bytes / 1024 / 1024 / 1024).ToString("N2", CultureInfo.CurrentCulture) + " GB";

    private static string Rate(double bytesPerSecond) =>
        bytesPerSecond >= 1024 * 1024
            ? (bytesPerSecond / 1024 / 1024).ToString("N2", CultureInfo.CurrentCulture) + " MB/s"
            : (bytesPerSecond / 1024).ToString("N1", CultureInfo.CurrentCulture) + " KB/s";

    private string BatteryStatusWord(BatteryStatus status) => status switch
    {
        BatteryStatus.Charging => Text("SysInfo.Battery.Charging", "charging"),
        BatteryStatus.Discharging => Text("SysInfo.Battery.Discharging", "discharging"),
        BatteryStatus.Full => Text("SysInfo.Battery.Full", "full"),
        BatteryStatus.NotCharging => Text("SysInfo.Battery.NotCharging", "not charging"),
        _ => Text("SysInfo.Battery.Unknown", "unknown"),
    };

    private string Duration(TimeSpan? time) =>
        time is { } span ? $"{(int)span.TotalHours}:{span.Minutes:00} h" : Text("SysInfo.State.NoTime", "no time");

    internal string BuildReport(SystemSnapshot s, bool failing)
    {
        var sb = new StringBuilder();

        sb.AppendLine(Header("SysInfo.Section.OperatingSystem", "Operating System"));
        sb.AppendLine($"  {Label("SysInfo.Label.OsDescription", "OS Description")}: {Value(s.OperatingSystem, v => v)}");
        sb.AppendLine($"  {Label("SysInfo.Label.Architecture", "OS Architecture")}: {Value(s.Architecture, v => v)}");
        sb.AppendLine($"  {Label("SysInfo.Label.Framework", "Framework")}: {Value(s.Framework, v => v)}");
        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.Hardware", "Hardware"));
        sb.AppendLine($"  {Label("SysInfo.Label.MachineName", "Machine Name")}: {Environment.MachineName}");
        sb.AppendLine($"  {Label("SysInfo.Label.ProcessArch", "Process Arch")}: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"  {Label("SysInfo.Label.Process64Bit", "64-bit Process")}: {Environment.Is64BitProcess}");
        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.Cpu", "Processor"));
        sb.AppendLine($"  {Label("SysInfo.Label.Model", "Model")}: {Value(s.CpuModel, v => v)}");
        sb.AppendLine($"  {Label("SysInfo.Label.LogicalProcessors", "Logical processors")}: {Value(s.LogicalProcessors, v => v.ToString(CultureInfo.CurrentCulture))}");
        sb.AppendLine($"  {Label("SysInfo.Label.Load", "Load")}: {Value(s.CpuLoadPercent, v => v.ToString("N0", CultureInfo.CurrentCulture) + " %")}");
        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.Memory", "Memory"));
        sb.AppendLine($"  {Label("SysInfo.Label.PhysicalTotal", "Physical Memory (total)")}: {Value(s.PhysicalMemoryTotal, v => Gigabytes(v))}");
        sb.AppendLine($"  {Label("SysInfo.Label.PhysicalAvailable", "Available Memory")}: {Value(s.PhysicalMemoryAvailable, v => Gigabytes(v))}");
        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.Gpu", "Graphics"));
        if (s.Gpus.State == ProbeState.Ok && s.Gpus.Value is { Count: > 0 } gpus)
        {
            foreach (GpuReading gpu in gpus)
            {
                sb.AppendLine($"  {Label("SysInfo.Label.Name", "Name")}: {gpu.Name}");
                sb.AppendLine($"  {Label("SysInfo.Label.Driver", "Driver")}: {gpu.Driver ?? StateWord(ProbeState.NotReported)}");
                sb.AppendLine($"  {Label("SysInfo.Label.VideoMemory", "Video memory")}: {(gpu.VideoMemoryBytes is { } vram ? Gigabytes(vram) : StateWord(ProbeState.NotReported))}");
            }
        }
        else
        {
            sb.AppendLine($"  {Label("SysInfo.Label.Name", "Name")}: {StateWord(s.Gpus.State)}");
        }

        sb.AppendLine($"  {Label("SysInfo.Label.Load", "Load")}: {Value(s.GpuLoadPercent, v => v.ToString("N0", CultureInfo.CurrentCulture) + " %")}");
        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.Battery", "Battery"));
        if (s.BatteryNames.State == ProbeState.Ok && s.BatteryNames.Value is { Count: 0 })
        {
            sb.AppendLine("  " + Text("SysInfo.Battery.None", "no battery"));
        }
        else if (s.BatteryNames.State != ProbeState.Ok)
        {
            sb.AppendLine("  " + StateWord(s.BatteryNames.State));
        }

        foreach (NamedProbe<BatteryReading> battery in s.Batteries)
        {
            if (battery.Value.State != ProbeState.Ok || battery.Value.Value is not { } b)
            {
                sb.AppendLine($"  {battery.Name}: {StateWord(battery.Value.State)}");
                continue;
            }

            sb.AppendLine($"  {Label("SysInfo.Label.Name", "Name")}: {battery.Name}");
            sb.AppendLine($"  {Label("SysInfo.Label.Status", "Status")}: {BatteryStatusWord(b.Status)}");
            sb.AppendLine($"  {Label("SysInfo.Label.Percent", "Charge")}: {(b.Percent is { } p ? p.ToString("N0", CultureInfo.CurrentCulture) + " %" : StateWord(ProbeState.NotReported))}");
            sb.AppendLine($"  {Label("SysInfo.Label.Remaining", "Time remaining")}: {Duration(b.TimeRemaining)}");
            if (b.Status == BatteryStatus.Charging)
            {
                sb.AppendLine($"  {Label("SysInfo.Label.ToFull", "Time to full")}: {Duration(b.TimeToFull)}");
            }

            if (b.PowerWatts is { } watts)
            {
                sb.AppendLine($"  {Label("SysInfo.Label.Power", "Power")}: {watts.ToString("N2", CultureInfo.CurrentCulture)} W");
            }

            if (b.DesignWattHours is { } design && b.FullWattHours is { } full && design > 0)
            {
                sb.AppendLine($"  {Label("SysInfo.Label.Health", "Health")}: {(full / design * 100).ToString("N0", CultureInfo.CurrentCulture)} %");
            }
            else
            {
                sb.AppendLine($"  {Label("SysInfo.Label.Health", "Health")}: {StateWord(ProbeState.NotReported)}");
            }

            if (b.Cycles is { } cycles)
            {
                sb.AppendLine($"  {Label("SysInfo.Label.Cycles", "Cycle count")}: {cycles}");
            }

            if (b.Manufacturer is not null || b.Model is not null)
            {
                sb.AppendLine($"  {Label("SysInfo.Label.Model", "Model")}: {b.Manufacturer} {b.Model}".TrimEnd());
            }
        }

        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.Drives", "Drives"));
        if (s.DriveNames.State != ProbeState.Ok)
        {
            sb.AppendLine("  " + StateWord(s.DriveNames.State));
        }

        foreach (NamedProbe<DriveReading> drive in s.Drives)
        {
            sb.AppendLine(DriveLine(drive));
        }

        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.DiskIo", "Disk I/O"));
        if (s.DiskNames.State != ProbeState.Ok)
        {
            sb.AppendLine("  " + StateWord(s.DiskNames.State));
        }

        foreach (NamedProbe<DiskRate> disk in s.Disks)
        {
            sb.AppendLine($"  {disk.Name,-16} {RateLine(disk.Value, d => Format("SysInfo.Io.Read", "Read {0}", Rate(d.ReadBytesPerSecond)), d => Format("SysInfo.Io.Write", "Write {0}", Rate(d.WriteBytesPerSecond)))}");
        }

        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.NetworkIo", "Network I/O"));
        if (s.NetworkNames.State != ProbeState.Ok)
        {
            sb.AppendLine("  " + StateWord(s.NetworkNames.State));
        }

        foreach (NamedProbe<NetworkRate> network in s.Network)
        {
            sb.AppendLine($"  {network.Name,-30} {RateLine(network.Value, n => Format("SysInfo.Io.Received", "Received {0}", Rate(n.ReceivedBytesPerSecond)), n => Format("SysInfo.Io.Sent", "Sent {0}", Rate(n.SentBytesPerSecond)))}");
        }

        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.NetworkInterfaces", "Network Interfaces"));
        if (s.Interfaces.State == ProbeState.Ok && s.Interfaces.Value is { } interfaces)
        {
            foreach (InterfaceReading nic in interfaces)
            {
                sb.AppendLine($"  {nic.Name,-30}  {Label("SysInfo.Label.Type", "Type")}: {nic.Type,-18}  {Label("SysInfo.Label.Status", "Status")}: {nic.Status}");
                foreach (string address in nic.Addresses)
                {
                    sb.AppendLine($"    {string.Empty,30}  {Label("SysInfo.Label.Address", "Address")}: {address}");
                }
            }
        }
        else
        {
            sb.AppendLine("  " + StateWord(s.Interfaces.State));
        }

        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.Runtime", "Runtime"));
        sb.AppendLine($"  {Label("SysInfo.Label.RuntimeId", "Runtime ID")}: {RuntimeInformation.RuntimeIdentifier}");
        sb.AppendLine($"  {Label("SysInfo.Label.ClrVersion", "CLR Version")}: {Environment.Version}");
        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.CurrentUser", "Current User"));
        sb.AppendLine($"  {Label("SysInfo.Label.UserName", "User Name")}: {Value(s.UserName, v => v)}");
        sb.AppendLine($"  {Label("SysInfo.Label.Domain", "User Domain")}: {Value(s.UserDomain, v => v)}");
        sb.AppendLine($"  {Label("SysInfo.Label.Home", "Home Directory")}: {Value(s.HomeFolder, v => v)}");
        sb.AppendLine();

        sb.AppendLine(Header("SysInfo.Section.Environment", "Environment Variables"));
        if (s.EnvironmentVariables.State == ProbeState.Ok && s.EnvironmentVariables.Value is { } variables)
        {
            foreach (KeyValuePair<string, string> variable in variables)
            {
                sb.AppendLine($"  {variable.Key,-40} = {variable.Value}");
            }
        }
        else
        {
            sb.AppendLine("  " + StateWord(s.EnvironmentVariables.State));
        }

        if (failing)
        {
            sb.AppendLine();
            sb.AppendLine("  " + StaleText());
        }

        return sb.ToString();
    }

    private string DriveLine(NamedProbe<DriveReading> drive)
    {
        if (drive.Value.State != ProbeState.Ok || drive.Value.Value is not { } d)
        {
            return $"  {drive.Name,-6} {StateWord(drive.Value.State)}";
        }

        if (!d.IsReady)
        {
            return $"  {d.Name,-6} {d.DriveType,-10} ({Text("SysInfo.State.NotReady", "not ready")})";
        }

        double used = d.TotalBytes - d.FreeBytes;
        return $"  {d.Name,-6} {d.DriveType,-10} {d.Format,-8}  " +
               $"{Text("SysInfo.Label.Total", "Total")}: {Gigabytes(d.TotalBytes)}  " +
               $"{Text("SysInfo.Label.Used", "Used")}: {Gigabytes(used)}  " +
               $"{Text("SysInfo.Label.Free", "Free")}: {Gigabytes(d.FreeBytes)}  " +
               $"{BarChart.BuildBar(d.TotalBytes == 0 ? 0 : used / d.TotalBytes, 10).Replace(' ', '░')}  " +
               $"{Text("SysInfo.Label.VolumeLabel", "Label")}: {d.Label}";
    }

    private string RateLine<T>(Probed<T> probed, Func<T, string> first, Func<T, string> second) =>
        probed.State == ProbeState.Ok && probed.Value is { } value
            ? $"{first(value)}  {second(value)}"
            : StateWord(probed.State);
}
