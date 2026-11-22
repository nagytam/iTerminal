using System.Threading;
using iConsole.Services;
using iTerminal.Core.Services.SystemInfo;

namespace iTerminal.Core.Services.SystemInfo.Test;

/// <summary>
/// Probes the test controls. A disk listed in <see cref="HangingDisks"/> blocks its read until <see cref="Gate"/> is
/// set, which is how a device that never answers is simulated. Every read is counted.
/// </summary>
internal sealed class FakeSystemProbes : ISystemProbes
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, int> _diskCalls = new(StringComparer.Ordinal);

    public ManualResetEventSlim Gate { get; } = new(false);

    public HashSet<string> HangingDisks { get; } = new(StringComparer.Ordinal);

    /// <summary>Holds the processor-model read until it is set, so a test decides when identity answers. Open by default.</summary>
    public ManualResetEventSlim IdentityGate { get; } = new(true);

    public List<string> DiskList { get; set; } = [];

    public int CpuModelCalls { get; private set; }

    public int CpuTickCalls { get; private set; }

    public int CpuTickValue { get; set; } = 1000;

    public int DiskCalls(string name)
    {
        lock (_lock)
        {
            return _diskCalls.GetValueOrDefault(name);
        }
    }

    /// <summary>Waits until a blocked disk read has been released and has returned.</summary>
    public async Task WaitForReturnAsync()
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                if (ReturnedReads > 0)
                {
                    return;
                }
            }

            await Task.Delay(5);
        }

        throw new TimeoutException("The blocked disk read never returned.");
    }

    private int ReturnedReads { get; set; }

    private readonly HashSet<string> _returnedDisks = new(StringComparer.Ordinal);

    /// <summary>Waits until a read of the named disk has returned, whether or not it was held.</summary>
    public async Task WaitForDiskReturnAsync(string name)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                if (_returnedDisks.Contains(name))
                {
                    return;
                }
            }

            await Task.Delay(5);
        }

        throw new TimeoutException($"The read of {name} never returned.");
    }

    public string OperatingSystem() => "Test OS";

    public string Architecture() => "X64";

    public string Framework() => ".NET test";

    public string UserName() => "tester";

    public string UserDomain() => "TESTS";

    public string HomeFolder() => "/home/tester";

    public IReadOnlyList<KeyValuePair<string, string>> EnvironmentVariables() => [new("PATH", "/bin")];

    public string CpuModel()
    {
        lock (_lock)
        {
            CpuModelCalls++;
        }

        IdentityGate.Wait();
        return "Test CPU";
    }

    public int LogicalProcessors() => 4;

    public CpuTicks ReadCpuTicks()
    {
        lock (_lock)
        {
            CpuTickCalls++;
        }

        return new CpuTicks(800, (ulong)CpuTickValue);
    }

    public MemoryReading ReadMemory() => new(16UL * 1024 * 1024 * 1024, 8UL * 1024 * 1024 * 1024);

    public IReadOnlyList<GpuReading> ReadGpus() => [];

    public double? ReadGpuLoadPercent() => null;

    public IReadOnlyList<string> DriveNames() => [];

    public DriveReading ReadDrive(string name) => new(name, "Fixed", "ext4", string.Empty, true, 100, 50);

    public IReadOnlyList<InterfaceReading> ReadInterfaces() => [];

    public IReadOnlyList<string> BatteryNames() => [];

    public BatteryReading? ReadBattery(string name) => null;

    public IReadOnlyList<string> DiskNames() => DiskList;

    public DiskCounters ReadDisk(string name)
    {
        int call;
        lock (_lock)
        {
            call = _diskCalls.GetValueOrDefault(name) + 1;
            _diskCalls[name] = call;
        }

        if (HangingDisks.Contains(name))
        {
            Gate.Wait();
            lock (_lock)
            {
                ReturnedReads++;
            }
        }

        lock (_lock)
        {
            _returnedDisks.Add(name);
        }

        return new DiskCounters((ulong)call * 1000, 0);
    }

    public IReadOnlyList<string> NetworkNames() => [];

    public NetworkCounters ReadNetwork(string name) => new(0, 0);
}

internal sealed class FakeProbeFactory(ISystemProbes probes) : ISystemProbeFactory
{
    public ISystemProbes Create() => probes;
}

/// <summary>A focus service the test moves by hand.</summary>
internal sealed class FakeFocus : ITerminalFocusService
{
    private readonly object _lock = new();
    private TaskCompletionSource? _regained;
    private bool _hasFocus = true;

    public bool HasFocus
    {
        get
        {
            lock (_lock)
            {
                return _hasFocus;
            }
        }
    }

    public bool IsReporting => true;

    public event Action<bool>? FocusChanged;

    public event Action? FocusRegained;

    public void SetFocus(bool hasFocus)
    {
        TaskCompletionSource? regained = null;
        lock (_lock)
        {
            _hasFocus = hasFocus;
            if (hasFocus)
            {
                regained = _regained;
                _regained = null;
            }
        }

        regained?.TrySetResult();
    }

    public void Report(bool hasFocus) => SetFocus(hasFocus);

    public Task WaitForFocusAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource source;
        lock (_lock)
        {
            if (_hasFocus)
            {
                return Task.CompletedTask;
            }

            _regained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source = _regained;
        }

        cancellationToken.Register(() => source.TrySetCanceled());
        return source.Task;
    }
}

/// <summary>
/// Holds the sample interval until the test releases it, so each pass happens when the test says. By default any other
/// delay (a probe's time budget) is a real short delay, so a hung probe times out in the test without the test releasing it.
/// With <paramref name="holdBudgets"/> every budget is held too, and it runs out only when <see cref="ExpireBudgets"/> is
/// called, so no healthy probe can be cut short by a loaded runner.
/// </summary>
internal sealed class FakeIntervalClock(TimeSpan interval, bool holdBudgets = false) : ISampleClock
{
    private readonly object _lock = new();
    private readonly List<TaskCompletionSource> _held = [];
    private readonly List<TaskCompletionSource> _budgets = [];

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay != interval && !holdBudgets)
        {
            return Task.Delay(delay, cancellationToken);
        }

        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => source.TrySetCanceled());
        lock (_lock)
        {
            (delay == interval ? _held : _budgets).Add(source);
        }

        return source.Task;
    }

    /// <summary>Runs out every held budget, so each probe still waiting on one is abandoned.</summary>
    public void ExpireBudgets()
    {
        List<TaskCompletionSource> expired;
        lock (_lock)
        {
            expired = [.. _budgets];
            _budgets.Clear();
        }

        foreach (TaskCompletionSource source in expired)
        {
            source.TrySetResult();
        }
    }

    /// <summary>Lets the sampler's next pass run, once the sampler is waiting for it.</summary>
    public async Task ReleaseAsync()
    {
        TaskCompletionSource? source = await WaitForHeldAsync();
        source.TrySetResult();
    }

    private async Task<TaskCompletionSource> WaitForHeldAsync()
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                if (_held.Count > 0)
                {
                    TaskCompletionSource first = _held[0];
                    _held.RemoveAt(0);
                    return first;
                }
            }

            await Task.Delay(5);
        }

        throw new TimeoutException("The sampler never reached its sample interval.");
    }
}
