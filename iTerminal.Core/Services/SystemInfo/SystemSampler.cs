using iConsole.Services;
using Serilog;

namespace iTerminal.Core.Services.SystemInfo;

/// <summary>
/// Samples the machine for the system information report. The loop runs only while someone is watching, as the stock
/// watch loop does: <see cref="Watch"/> returns a handle, the first handle starts the loop and the last one stops it.
/// Every probe unit lives in the sampler, not in the loop, so a call that hung is still counted when the loop starts
/// again and is never repeated while it hangs.
/// </summary>
public interface ISystemSampler : IDisposable
{
    /// <summary>The latest report, or null until the first pass has finished.</summary>
    SystemSnapshot? Current { get; }

    /// <summary>True when the most recent pass failed, so <see cref="Current"/> may be out of date.</summary>
    bool PassFailing { get; }

    /// <summary>Starts the sampling loop while the returned handle is held. Dispose the handle to let go of it.</summary>
    IDisposable Watch();
}

public sealed class SystemSampler : ISystemSampler
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultLiveBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultIdentityBudget = TimeSpan.FromSeconds(10);

    private readonly ISystemProbes _probes;
    private readonly ITerminalFocusService _focus;
    private readonly ISampleClock _clock;
    private readonly ITimeProvider _time;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _liveBudget;
    private readonly TimeSpan _identityBudget;
    private readonly object _lock = new();
    private SystemSnapshot? _current;
    private bool _passFailing;
    private int _watchers;
    private CancellationTokenSource? _loopCancellation;
    private bool _disposed;

    // Identity: read once, until each answers.
    private readonly IdentityUnit<string> _operatingSystem;
    private readonly IdentityUnit<string> _architecture;
    private readonly IdentityUnit<string> _framework;
    private readonly IdentityUnit<string> _userName;
    private readonly IdentityUnit<string> _userDomain;
    private readonly IdentityUnit<string> _homeFolder;
    private readonly IdentityUnit<IReadOnlyList<KeyValuePair<string, string>>> _environment;
    private readonly IdentityUnit<string> _cpuModel;
    private readonly IdentityUnit<int> _logicalProcessors;
    private readonly IdentityUnit<IReadOnlyList<GpuReading>> _gpus;

    // Live: every pass.
    private readonly ProbeUnit<Timed<CpuTicks>> _cpu;
    private readonly ProbeUnit<MemoryReading> _memory;
    private readonly ProbeUnit<double?> _gpuLoad;
    private readonly ProbeUnit<IReadOnlyList<string>> _driveNames;
    private readonly ProbeUnit<IReadOnlyList<InterfaceReading>> _interfaces;
    private readonly ProbeUnit<IReadOnlyList<string>> _batteryNames;
    private readonly ProbeUnit<IReadOnlyList<string>> _diskNames;
    private readonly ProbeUnit<IReadOnlyList<string>> _networkNames;

    // Per item: one unit each, created when the item is first listed and kept for the life of the sampler.
    private readonly Dictionary<string, ProbeUnit<DriveReading>> _drives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProbeUnit<BatteryReading?>> _batteries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProbeUnit<Timed<DiskCounters>>> _disks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProbeUnit<Timed<NetworkCounters>>> _networks = new(StringComparer.Ordinal);

    // The names from the last listing that answered, and the last accepted reads. Rates are taken between accepted reads
    // of the same run of the loop only.
    private IReadOnlyList<string> _driveNameList = [];
    private IReadOnlyList<string> _batteryNameList = [];
    private IReadOnlyList<string> _diskNameList = [];
    private IReadOnlyList<string> _networkNameList = [];
    private Timed<CpuTicks>? _previousCpu;
    private readonly Dictionary<string, Timed<DiskCounters>> _previousDisk = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Timed<NetworkCounters>> _previousNetwork = new(StringComparer.Ordinal);

    // One pass at a time, whichever loop runs it: a loop that was stopped may still be finishing its pass when a new loop
    // starts, and the passes share the rate state.
    private readonly SemaphoreSlim _passGate = new(1, 1);
    private readonly ILogger _logger;

    public SystemSampler(
        ISystemProbeFactory probeFactory,
        ITerminalFocusService focus,
        ISampleClock clock,
        ITimeProvider time,
        ILogger logger,
        TimeSpan? interval = null,
        TimeSpan? liveBudget = null,
        TimeSpan? identityBudget = null)
    {
        _probes = probeFactory.Create();
        _focus = focus;
        _clock = clock;
        _time = time;
        _logger = logger;
        _interval = interval ?? DefaultInterval;
        _liveBudget = liveBudget ?? DefaultLiveBudget;
        _identityBudget = identityBudget ?? DefaultIdentityBudget;

        ISystemProbes probes = _probes;
        _operatingSystem = new IdentityUnit<string>(new ProbeUnit<string>(probes.OperatingSystem));
        _architecture = new IdentityUnit<string>(new ProbeUnit<string>(probes.Architecture));
        _framework = new IdentityUnit<string>(new ProbeUnit<string>(probes.Framework));
        _userName = new IdentityUnit<string>(new ProbeUnit<string>(probes.UserName));
        _userDomain = new IdentityUnit<string>(new ProbeUnit<string>(probes.UserDomain));
        _homeFolder = new IdentityUnit<string>(new ProbeUnit<string>(probes.HomeFolder));
        _environment = new IdentityUnit<IReadOnlyList<KeyValuePair<string, string>>>(new ProbeUnit<IReadOnlyList<KeyValuePair<string, string>>>(probes.EnvironmentVariables));
        _cpuModel = new IdentityUnit<string>(new ProbeUnit<string>(probes.CpuModel));
        _logicalProcessors = new IdentityUnit<int>(new ProbeUnit<int>(probes.LogicalProcessors));
        _gpus = new IdentityUnit<IReadOnlyList<GpuReading>>(new ProbeUnit<IReadOnlyList<GpuReading>>(probes.ReadGpus));

        _cpu = new ProbeUnit<Timed<CpuTicks>>(() => new Timed<CpuTicks>(probes.ReadCpuTicks(), _time.MonotonicMilliseconds));
        _memory = new ProbeUnit<MemoryReading>(probes.ReadMemory);
        _gpuLoad = new ProbeUnit<double?>(probes.ReadGpuLoadPercent);
        _driveNames = new ProbeUnit<IReadOnlyList<string>>(probes.DriveNames);
        _interfaces = new ProbeUnit<IReadOnlyList<InterfaceReading>>(probes.ReadInterfaces);
        _batteryNames = new ProbeUnit<IReadOnlyList<string>>(probes.BatteryNames);
        _diskNames = new ProbeUnit<IReadOnlyList<string>>(probes.DiskNames);
        _networkNames = new ProbeUnit<IReadOnlyList<string>>(probes.NetworkNames);
    }

    public SystemSnapshot? Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public bool PassFailing
    {
        get
        {
            lock (_lock)
            {
                return _passFailing;
            }
        }
    }

    public IDisposable Watch()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SystemSampler));
            }

            if (++_watchers == 1)
            {
                _loopCancellation = new CancellationTokenSource();
                CancellationToken token = _loopCancellation.Token;
                _ = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
            }
        }

        return new Watcher(this);
    }

    /// <summary>
    /// Stops the loop and does not wait for it: a probe still hung on a worker thread is not awaited, so shutdown is
    /// never held up by a device that does not answer.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _loopCancellation?.Cancel();
        }
    }

    private void StopWatching()
    {
        lock (_lock)
        {
            if (_watchers == 0 || --_watchers > 0)
            {
                return;
            }

            _loopCancellation?.Cancel();
            _loopCancellation?.Dispose();
            _loopCancellation = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The first pass of a run has no earlier read to take a rate from, and neither has the first pass after the
            // terminal regains focus: a rate over a gap in sampling would be an average of the gap.
            bool restart = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                bool unfocused = !_focus.HasFocus;
                await _focus.WaitForFocusAsync(cancellationToken);
                restart |= unfocused;

                SystemSnapshot? snapshot = await RunPassAsync(restart, cancellationToken);

                // A stopped loop finishes its pass but does not publish it.
                if (snapshot is not null && !cancellationToken.IsCancellationRequested)
                {
                    lock (_lock)
                    {
                        _current = snapshot;
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    lock (_lock)
                    {
                        _passFailing = snapshot is null;
                    }
                }

                // A pass that failed leaves the rate state unknown, so the next pass measures again.
                restart = snapshot is null;

                await _clock.Delay(_interval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The loop was stopped: the last handle was let go, or the sampler was disposed.
        }
        catch (Exception exception)
        {
            // Nothing awaits the loop, so an exception from the focus service or the clock would otherwise be lost.
            _logger.Error(exception, "The system information sampler stopped.");
        }
    }

    /// <summary>
    /// One pass, under the pass gate. An unexpected exception is logged and the pass is given up, so one bad pass does
    /// not end the loop while handles are still held.
    /// </summary>
    private async Task<SystemSnapshot?> RunPassAsync(bool restart, CancellationToken cancellationToken)
    {
        await _passGate.WaitAsync(cancellationToken);
        try
        {
            return await PassAsync(restart, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "The system information sampler could not complete a pass.");
            return null;
        }
        finally
        {
            _passGate.Release();
        }
    }

    private async Task<SystemSnapshot> PassAsync(bool restart, CancellationToken cancellationToken)
    {
        if (restart)
        {
            _previousCpu = null;
            _previousDisk.Clear();
            _previousNetwork.Clear();
        }

        // Every unit due in this pass starts together, so the pass takes one budget, not the sum of them.
        var pending = new List<Task>();
        Task<Probed<Timed<CpuTicks>>> cpu = Start(_cpu, cancellationToken, pending);
        Task<Probed<MemoryReading>> memory = Start(_memory, cancellationToken, pending);
        Task<Probed<double?>> gpuLoad = Start(_gpuLoad, cancellationToken, pending);
        Task<Probed<IReadOnlyList<string>>> driveNames = Start(_driveNames, cancellationToken, pending);
        Task<Probed<IReadOnlyList<InterfaceReading>>> interfaces = Start(_interfaces, cancellationToken, pending);
        Task<Probed<IReadOnlyList<string>>> batteryNames = Start(_batteryNames, cancellationToken, pending);
        Task<Probed<IReadOnlyList<string>>> diskNames = Start(_diskNames, cancellationToken, pending);
        Task<Probed<IReadOnlyList<string>>> networkNames = Start(_networkNames, cancellationToken, pending);

        var driveTasks = _driveNameList.Select(name => (name, Start(DriveUnit(name), cancellationToken, pending))).ToList();
        var batteryTasks = _batteryNameList.Select(name => (name, Start(BatteryUnit(name), cancellationToken, pending))).ToList();
        var diskTasks = _diskNameList.Select(name => (name, Start(DiskUnit(name), cancellationToken, pending))).ToList();
        var networkTasks = _networkNameList.Select(name => (name, Start(NetworkUnit(name), cancellationToken, pending))).ToList();

        await Task.WhenAll(pending);

        // Identity is never waited for: its units run on their own budget and the report fills in as they answer.
        Probed<string> operatingSystem = _operatingSystem.Current(_clock, _identityBudget, cancellationToken);
        Probed<string> architecture = _architecture.Current(_clock, _identityBudget, cancellationToken);
        Probed<string> framework = _framework.Current(_clock, _identityBudget, cancellationToken);
        Probed<string> userName = _userName.Current(_clock, _identityBudget, cancellationToken);
        Probed<string> userDomain = _userDomain.Current(_clock, _identityBudget, cancellationToken);
        Probed<string> homeFolder = _homeFolder.Current(_clock, _identityBudget, cancellationToken);
        Probed<IReadOnlyList<KeyValuePair<string, string>>> environment = _environment.Current(_clock, _identityBudget, cancellationToken);
        Probed<string> cpuModel = _cpuModel.Current(_clock, _identityBudget, cancellationToken);
        Probed<int> logicalProcessors = _logicalProcessors.Current(_clock, _identityBudget, cancellationToken);
        Probed<IReadOnlyList<GpuReading>> gpus = _gpus.Current(_clock, _identityBudget, cancellationToken);

        Probed<double> cpuLoad = CpuLoad(cpu.Result);
        Probed<ulong> total = memory.Result.State == ProbeState.Ok ? Probed<ulong>.Ok(memory.Result.Value!.TotalBytes) : Retag<MemoryReading, ulong>(memory.Result.State);
        Probed<ulong> available = memory.Result.State == ProbeState.Ok ? Probed<ulong>.Ok(memory.Result.Value!.AvailableBytes) : Retag<MemoryReading, ulong>(memory.Result.State);

        Probed<IReadOnlyList<string>> driveNamesResult = driveNames.Result;
        if (driveNamesResult.State == ProbeState.Ok)
        {
            _driveNameList = driveNamesResult.Value!;
        }

        Probed<IReadOnlyList<string>> batteryNamesResult = batteryNames.Result;
        if (batteryNamesResult.State == ProbeState.Ok)
        {
            _batteryNameList = batteryNamesResult.Value!;
        }

        Probed<IReadOnlyList<string>> diskNamesResult = diskNames.Result;
        if (diskNamesResult.State == ProbeState.Ok)
        {
            _diskNameList = diskNamesResult.Value!;
        }

        Probed<IReadOnlyList<string>> networkNamesResult = networkNames.Result;
        if (networkNamesResult.State == ProbeState.Ok)
        {
            _networkNameList = networkNamesResult.Value!;
        }

        // A device that is no longer listed is forgotten, so a device that later takes its name starts from nothing.
        // A unit whose call is still outstanding is kept until that call returns, so a hung call is not repeated.
        PruneUnits(_drives, _driveNameList, StringComparer.OrdinalIgnoreCase);
        PruneUnits(_batteries, _batteryNameList, StringComparer.Ordinal);
        PruneUnits(_disks, _diskNameList, StringComparer.Ordinal);
        Prune(_previousDisk, _diskNameList, StringComparer.Ordinal);
        PruneUnits(_networks, _networkNameList, StringComparer.Ordinal);
        Prune(_previousNetwork, _networkNameList, StringComparer.Ordinal);

        Probed<double> gpuValue;
        if (gpuLoad.Result.State == ProbeState.Ok && gpuLoad.Result.Value is { } gpuPercent) gpuValue = Probed<double>.Ok(gpuPercent);
        else if (gpuLoad.Result.State == ProbeState.Ok) gpuValue = Probed<double>.NotReported;
        else gpuValue = Retag<double?, double>(gpuLoad.Result.State);
        return new SystemSnapshot(
            operatingSystem,
            architecture,
            framework,
            userName,
            userDomain,
            homeFolder,
            environment,
            cpuModel,
            logicalProcessors,
            cpuLoad,
            total,
            available,
            gpus,
            gpuValue,
            driveNamesResult,
            driveTasks.Select(item => new NamedProbe<DriveReading>(item.name, item.Item2.Result)).ToList(),
            interfaces.Result,
            batteryNamesResult,
            batteryTasks.Select(item => new NamedProbe<BatteryReading>(item.name, BatteryValue(item.Item2.Result))).ToList(),
            diskNamesResult,
            diskTasks.Select(item => new NamedProbe<DiskRate>(item.name, DiskValue(item.name, item.Item2.Result))).ToList(),
            networkNamesResult,
            networkTasks.Select(item => new NamedProbe<NetworkRate>(item.name, NetworkValue(item.name, item.Item2.Result))).ToList());
    }

    private static void Prune<TValue>(Dictionary<string, TValue> entries, IReadOnlyList<string> names, IEqualityComparer<string> comparer)
    {
        var listed = new HashSet<string>(names, comparer);
        foreach (string key in entries.Keys.Where(key => !listed.Contains(key)).ToList())
        {
            entries.Remove(key);
        }
    }

    private static void PruneUnits<TValue>(Dictionary<string, ProbeUnit<TValue>> units, IReadOnlyList<string> names, IEqualityComparer<string> comparer)
    {
        var listed = new HashSet<string>(names, comparer);
        foreach (string key in units.Where(entry => !listed.Contains(entry.Key) && !entry.Value.IsBusy).Select(entry => entry.Key).ToList())
        {
            units.Remove(key);
        }
    }

    private Task<Probed<T>> Start<T>(ProbeUnit<T> unit, CancellationToken cancellationToken, List<Task> pending)
    {
        Task<Probed<T>> task = unit.RunAsync(_clock, _liveBudget, cancellationToken);
        pending.Add(task);
        return task;
    }

    private ProbeUnit<DriveReading> DriveUnit(string name)
    {
        if (!_drives.TryGetValue(name, out ProbeUnit<DriveReading>? unit))
        {
            ISystemProbes probes = _probes;
            unit = new ProbeUnit<DriveReading>(() => probes.ReadDrive(name));
            _drives[name] = unit;
        }

        return unit;
    }

    private ProbeUnit<BatteryReading?> BatteryUnit(string name)
    {
        if (!_batteries.TryGetValue(name, out ProbeUnit<BatteryReading?>? unit))
        {
            ISystemProbes probes = _probes;
            unit = new ProbeUnit<BatteryReading?>(() => probes.ReadBattery(name));
            _batteries[name] = unit;
        }

        return unit;
    }

    private ProbeUnit<Timed<DiskCounters>> DiskUnit(string name)
    {
        if (!_disks.TryGetValue(name, out ProbeUnit<Timed<DiskCounters>>? unit))
        {
            ISystemProbes probes = _probes;
            unit = new ProbeUnit<Timed<DiskCounters>>(() => new Timed<DiskCounters>(probes.ReadDisk(name), _time.MonotonicMilliseconds));
            _disks[name] = unit;
        }

        return unit;
    }

    private ProbeUnit<Timed<NetworkCounters>> NetworkUnit(string name)
    {
        if (!_networks.TryGetValue(name, out ProbeUnit<Timed<NetworkCounters>>? unit))
        {
            ISystemProbes probes = _probes;
            unit = new ProbeUnit<Timed<NetworkCounters>>(() => new Timed<NetworkCounters>(probes.ReadNetwork(name), _time.MonotonicMilliseconds));
            _networks[name] = unit;
        }

        return unit;
    }

    private Probed<double> CpuLoad(Probed<Timed<CpuTicks>> read)
    {
        if (read.State != ProbeState.Ok)
        {
            // The next answer is a rate over a gap, so it is measured again rather than averaged over the gap.
            _previousCpu = null;
            return Retag<Timed<CpuTicks>, double>(read.State);
        }

        Timed<CpuTicks> current = read.Value!;
        Timed<CpuTicks>? previous = _previousCpu;
        _previousCpu = current;
        if (previous is null)
        {
            return Probed<double>.Measuring;
        }

        return SystemInfoParsers.CpuLoadPercent(previous.Value, current.Value) is { } load
            ? Probed<double>.Ok(load)
            : Probed<double>.Measuring;
    }

    private static Probed<BatteryReading> BatteryValue(Probed<BatteryReading?> read)
    {
        if (read.State == ProbeState.Ok && read.Value is { } battery) return Probed<BatteryReading>.Ok(battery);
        return read.State == ProbeState.Ok ? Probed<BatteryReading>.NotReported : Retag<BatteryReading?, BatteryReading>(read.State);
    }

    private Probed<DiskRate> DiskValue(string name, Probed<Timed<DiskCounters>> read)
    {
        if (read.State != ProbeState.Ok)
        {
            _previousDisk.Remove(name);
            return Retag<Timed<DiskCounters>, DiskRate>(read.State);
        }

        Timed<DiskCounters> current = read.Value!;
        bool hadPrevious = _previousDisk.TryGetValue(name, out Timed<DiskCounters>? previous);
        _previousDisk[name] = current;
        if (!hadPrevious || previous is null)
        {
            return Probed<DiskRate>.Measuring;
        }

        double? reads = SystemInfoParsers.PerSecond(previous.Value.ReadBytes, current.Value.ReadBytes, previous.Milliseconds, current.Milliseconds);
        double? writes = SystemInfoParsers.PerSecond(previous.Value.WriteBytes, current.Value.WriteBytes, previous.Milliseconds, current.Milliseconds);
        return reads is { } r && writes is { } w ? Probed<DiskRate>.Ok(new DiskRate(r, w)) : Probed<DiskRate>.Measuring;
    }

    private Probed<NetworkRate> NetworkValue(string name, Probed<Timed<NetworkCounters>> read)
    {
        if (read.State != ProbeState.Ok)
        {
            _previousNetwork.Remove(name);
            return Retag<Timed<NetworkCounters>, NetworkRate>(read.State);
        }

        Timed<NetworkCounters> current = read.Value!;
        bool hadPrevious = _previousNetwork.TryGetValue(name, out Timed<NetworkCounters>? previous);
        _previousNetwork[name] = current;
        if (!hadPrevious || previous is null)
        {
            return Probed<NetworkRate>.Measuring;
        }

        double? received = SystemInfoParsers.PerSecond(previous.Value.ReceivedBytes, current.Value.ReceivedBytes, previous.Milliseconds, current.Milliseconds);
        double? sent = SystemInfoParsers.PerSecond(previous.Value.SentBytes, current.Value.SentBytes, previous.Milliseconds, current.Milliseconds);
        return received is { } r && sent is { } s ? Probed<NetworkRate>.Ok(new NetworkRate(r, s)) : Probed<NetworkRate>.Measuring;
    }

    /// <summary>A state that is not a value, carried over to another value type.</summary>
    #pragma warning disable S2326 // The type argument documents the source state; the generic keeps the call sites explicit.
    private static Probed<TTo> Retag<TFrom, TTo>(ProbeState state) => new(state, default);
    #pragma warning restore S2326

    private sealed class Watcher(SystemSampler owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.StopWatching();
            }
        }
    }
}
