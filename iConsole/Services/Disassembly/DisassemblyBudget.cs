using System.Diagnostics;

namespace iConsole.Services.Disassembly;

/// <summary>
/// The output of one disassembly run and the limits it keeps to. The file is untrusted, so a crafted one must not be
/// able to use unbounded memory or time: every disassembler asks here before it adds a line or starts another unit of work.
/// </summary>
internal sealed class DisassemblyBudget
{
    public const int MaxMethods = 2_000;
    public const int MaxInstructionsPerMethod = 5_000;
    public const int MaxLines = 100_000;
    public const int MaxNativeBytes = 4 * 1024 * 1024;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(5);

    private readonly CancellationToken _token;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TimeSpan _maxDuration;

    public DisassemblyBudget(CancellationToken token, TimeSpan? maxDuration = null)
    {
        _token = token;
        _maxDuration = maxDuration ?? MaxDuration;
    }

    public List<string> Lines { get; } = [];

    /// <summary>Why the output stops short, or null while nothing has run out.</summary>
    public string? TruncatedBecause { get; private set; }

    /// <summary>True once a limit is hit; the caller stops what it is doing and leaves the output as it is.</summary>
    public bool Exhausted
    {
        get
        {
            _token.ThrowIfCancellationRequested();
            if (TruncatedBecause is not null)
            {
                return true;
            }

            if (Lines.Count >= MaxLines)
            {
                TruncatedBecause = $"the {MaxLines:N0} line limit was reached";
            }
            else if (_clock.Elapsed > _maxDuration)
            {
                TruncatedBecause = $"the {_maxDuration.TotalSeconds:0.#} second time limit was reached";
            }

            return TruncatedBecause is not null;
        }
    }

    /// <summary>Adds a line unless the output is full. Returns false when the line was dropped.</summary>
    public bool Add(string line)
    {
        if (Exhausted)
        {
            return false;
        }

        Lines.Add(line);
        return true;
    }

    /// <summary>Records a limit that is specific to one unit of work (too many methods, too many bytes).</summary>
    public void Truncate(string reason) => TruncatedBecause ??= reason;

    /// <summary>Adds the closing note when anything was cut off.</summary>
    public void FinishTruncation()
    {
        if (TruncatedBecause is not null)
        {
            Lines.Add($"  … output truncated: {TruncatedBecause}.");
        }
    }
}
