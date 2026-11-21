using System.ComponentModel;
using System.Diagnostics;

namespace iConsole.Services;

/// <summary>
/// One process in a <see cref="ProcessService.Sample"/>: its name, the memory it holds and its share of the processor since the
/// previous sample. A value that the system would not give (access denied, the process ended) is null.
/// </summary>
/// <param name="WorkingSetBytes">The physical memory the process has in use.</param>
/// <param name="CpuPercent">The share of the whole processor the process used, 0 to 100; null until it has been sampled twice.</param>
public sealed record ProcessSample(int Id, string Name, long? WorkingSetBytes, double? CpuPercent);

/// <summary>
/// Lists the running processes with the memory and processor time each one uses. Processor use is measured between two calls to
/// <see cref="Sample"/>, so the first call reports none. A process that ends while it is being read is left out. Each call reads
/// the whole list again, so the caller decides how often to sample.
/// </summary>
public class ProcessService
{
    private readonly Lock _gate = new();
    private Dictionary<int, (long StartTicks, TimeSpan ProcessorTime)> _previous = [];
    private long _previousTimestamp;

    /// <summary>Reads every running process, ordered by id.</summary>
    public IReadOnlyList<ProcessSample> Sample()
    {
        lock (_gate)
        {
            long now = Stopwatch.GetTimestamp();
            double elapsedMilliseconds = _previousTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(_previousTimestamp, now).TotalMilliseconds;
            var current = new Dictionary<int, (long StartTicks, TimeSpan ProcessorTime)>();
            var samples = new List<ProcessSample>();
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    ProcessSample? sample = ReadOne(process, elapsedMilliseconds, current);
                    if (sample is not null)
                    {
                        samples.Add(sample);
                    }
                }
            }

            _previous = current;
            _previousTimestamp = now;
            return [.. samples.OrderBy(s => s.Id)];
        }
    }

    private ProcessSample? ReadOne(
        Process process,
        double elapsedMilliseconds,
        Dictionary<int, (long StartTicks, TimeSpan ProcessorTime)> current)
    {
        // The name is what makes a row; the other values are read on their own, so one that is denied still leaves the row.
        string? name = Read(() => process.ProcessName);
        if (name is null)
        {
            return null;
        }

        int id = process.Id;
        long? workingSet = Read(() => process.WorkingSet64);
        long? startTicks = Read(() => process.StartTime.Ticks);
        TimeSpan? processorTime = Read(() => process.TotalProcessorTime);

        double? cpuPercent = null;
        if (startTicks is { } start && processorTime is { } time)
        {
            current[id] = (start, time);
            // A process id can be reused by a new process, so the earlier reading counts only for the same start time.
            if (elapsedMilliseconds > 0 && _previous.TryGetValue(id, out var before) && before.StartTicks == start)
            {
                double used = (time - before.ProcessorTime).TotalMilliseconds;
                cpuPercent = Math.Clamp(used / elapsedMilliseconds / Environment.ProcessorCount * 100, 0, 100);
            }
        }

        return new ProcessSample(id, name, workingSet, cpuPercent);
    }

    private static string? Read(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (IsUnreadable(e))
        {
            return null;
        }
    }

    private static T? Read<T>(Func<T> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception e) when (IsUnreadable(e))
        {
            return null;
        }
    }

    private static bool IsUnreadable(Exception e) =>
        e is InvalidOperationException or Win32Exception or NotSupportedException;
}
