using iConsole.Buffers;
using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Display;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services.Benchmark;

namespace iTerminal.Core.Dialogs.Benchmark;

/// <summary>
/// Ctrl+Shift+B: a score for this machine's CPU, memory, disk, network and timer latency, one line per measurement and one
/// score per group, with an overall score that is the geometric mean of the groups. Enter runs it; Esc cancels a run that is
/// going and closes the dialog otherwise.
///
/// <para>The run is on a background task. Its progress lands in a snapshot under a lock, and <see cref="Draw"/> reads only that
/// snapshot, so no key handler or repaint waits for a measurement. The benchmark never writes to disk: the disk probes read an
/// existing program file.</para>
/// </summary>
public class PerformanceBenchmarkDialog : Dialog
{
    private const int PreferredWidth = 78;
    private const int BarWidth = 12;
    private const string SpaceSeparator = "  ";

    private enum RunState
    {
        Idle,
        Running,
        Finished,
        Cancelled,
    }

    private readonly IPerformanceBenchmarkService _service;
    private readonly object _gate = new();
    private readonly Dictionary<string, BenchmarkResult> _results = [];
    private RunState _state = RunState.Idle;
    private string? _running;
    private CancellationTokenSource? _cancellation;
    private Task _run = Task.CompletedTask;

    public PerformanceBenchmarkDialog(IPerformanceBenchmarkService service, IThemeService themeService)
        : base(nameof(PerformanceBenchmarkDialog), themeService)
    {
        _service = service;
        Title = UiText.Get("Dialog.PerformanceBenchmark", "Performance benchmark");
    }

    /// <summary>The run the last Enter started. Nothing awaits it: it exists so a test can wait for the result instead of polling.</summary>
    internal Task RunTask => _run;

    /// <summary>Whether a run is going now.</summary>
    internal bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _state == RunState.Running;
            }
        }
    }

    /// <summary>The scores so far, in the order the probes run. The overall score is null until something has been scored.</summary>
    internal BenchmarkReport CurrentReport
    {
        get
        {
            lock (_gate)
            {
                return ReportLocked();
            }
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            int width = (int)Math.Max(20, Math.Min(PreferredWidth, (long)Parent.ViewSize.Width));
            int height = (int)Math.Max(10, Math.Min(Rows(Math.Max(1, width - 2), ThemeService.CurrentColorScheme).Count + 4, (long)Parent.ViewSize.Height));
            CenterInParent(new Size(width, height));
        }

        base.OnInitializeLayout();
    }

    /// <summary>Starts a run unless one is going. The previous scores are cleared when the new run starts.</summary>
    public void StartRun()
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_state == RunState.Running)
            {
                return;
            }

            _results.Clear();
            _running = null;
            _state = RunState.Running;
            _cancellation = cancellation = new CancellationTokenSource();
        }

        _run = Task.Run(async () =>
        {
            bool complete = false;
            try
            {
                complete = (await _service.RunAsync(new InlineProgress<BenchmarkProgress>(Apply), cancellation.Token)).IsComplete;
            }
            catch (OperationCanceledException)
            {
                // A cancelled run keeps the scores it had.
            }
            finally
            {
                lock (_gate)
                {
                    _state = complete ? RunState.Finished : RunState.Cancelled;
                    _running = null;
                }
            }
        });
    }

    /// <summary>Stops the run that is going. The scores measured so far stay on screen.</summary>
    internal void CancelRun()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _state == RunState.Running ? _cancellation : null;
        }

        cancellation?.Cancel();
    }

    private void Apply(BenchmarkProgress progress)
    {
        lock (_gate)
        {
            if (progress.Result is null)
            {
                _running = progress.Name;
                return;
            }

            _results[progress.Result.Id] = progress.Result;
            _running = null;
        }
    }

    private BenchmarkReport ReportLocked()
    {
        List<BenchmarkResult> ordered = [.. _service.Probes.Where(probe => _results.ContainsKey(probe.Id)).Select(probe => _results[probe.Id])];
        return new BenchmarkReport(ordered, _state == RunState.Finished);
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        if (plain && keyEvent.ConsoleKey == ConsoleKey.Enter && !IsRunning)
        {
            StartRun();
            StateHasChanged();
            return true;
        }

        // Esc stops a run first; the next Esc closes the dialog.
        if (plain && keyEvent.ConsoleKey == ConsoleKey.Escape && IsRunning)
        {
            CancelRun();
            StateHasChanged();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Close()
    {
        CancelRun();
        base.Close();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (IsRunning)
        {
            yield return ("Esc", UiText.Get("Benchmark.Hint.CancelRun", "Cancel the run"));
            yield break;
        }

        yield return ("Enter", UiText.Get("Benchmark.Hint.Run", "Run the benchmark"));
        yield return ("Esc", UiText.Get("Common.Close", "Close"));
    }

    /// <summary>
    /// The rows of the dialog, built from the snapshot on every draw so a measurement that finished in the background shows on the
    /// next repaint.
    /// </summary>
    internal List<(string Text, Color Color)> Rows(int innerWidth, ColorScheme scheme)
    {
        RunState state;
        string? running;
        BenchmarkReport report;
        lock (_gate)
        {
            state = _state;
            running = _running;
            report = ReportLocked();
        }

        Dictionary<string, BenchmarkResult> byId = report.Results.ToDictionary(result => result.Id);
        IReadOnlyDictionary<BenchmarkCategory, double> categories = report.CategoryScores;

        List<(string Text, Color Color)> rows =
        [
            (PromptFor(state, running), state == RunState.Running ? Color.DarkYellow : scheme.Foreground),
            (string.Empty, scheme.Foreground),
            (
                $"{UiText.Get("Benchmark.Overall", "Overall score")}{SpaceSeparator}{ScoreText(report.Overall)}",
                report.Overall is null ? scheme.DisabledForeground : Color.DarkGreen),
            (UiText.Get("Benchmark.Scale", "Higher is better. 1 000 is a reference machine."), scheme.DisabledForeground),
            (string.Empty, scheme.Foreground),
        ];

        foreach (BenchmarkCategory category in Enum.GetValues<BenchmarkCategory>())
        {
            IBenchmarkProbe[] probes = [.. _service.Probes.Where(probe => probe.Category == category)];
            if (probes.Length == 0)
            {
                continue;
            }

            double? categoryScore = categories.TryGetValue(category, out double score) ? score : (double?)null;
            string header = CategoryName(category).PadRight(24) + ScoreText(categoryScore);
            rows.Add((header, scheme.Foreground));
            foreach (IBenchmarkProbe probe in probes)
            {
                rows.Add(ProbeRow(probe, byId.GetValueOrDefault(probe.Id), running, state, scheme));
            }

            rows.Add((string.Empty, scheme.Foreground));
        }

        rows.Add((UiText.Get("Benchmark.Note.Disk", "Disk: reads one program file, up to 256 MB. Cached data reads faster."), scheme.DisabledForeground));
        rows.Add((UiText.Get("Benchmark.Note.Network", "Internet tests download from speed.cloudflare.com (10 MB)."), scheme.DisabledForeground));

        return rows.Select(row => (row.Text.Length > innerWidth ? row.Text.Cut(innerWidth) : row.Text, row.Color)).ToList();
    }

    private (string Text, Color Color) ProbeRow(IBenchmarkProbe probe, BenchmarkResult? result, string? running, RunState state, ColorScheme scheme)
    {
        string label = SpaceSeparator + ProbeName(probe).PadRight(24);
        if (result is not null && result.Score is { } score)
        {
            string value = $"{FormatValue(result.Measurement.Value!.Value)} {result.Measurement.Unit}".PadRight(18);
            return ($"{label}{value}{ScoreText(score).PadLeft(7)}  {BarChart.BuildBar(Math.Min(score / (2 * BenchmarkScore.ReferenceScore), 1), BarWidth)}", scheme.Foreground);
        }

        if (result is not null)
        {
            return ($"{label}{UiText.Get("Benchmark.Unavailable", "unavailable")}: {result.Measurement.Reason}", scheme.DisabledForeground);
        }

        if (state == RunState.Running && running == probe.Name)
        {
            return ($"{label}{UiText.Get("Benchmark.Measuring", "measuring...")}", Color.DarkYellow);
        }

        return ($"{label}{UiText.Get("Benchmark.Waiting", "waiting")}", scheme.DisabledForeground);
    }

    private static string PromptFor(RunState state, string? running) => state switch
    {
        RunState.Running => string.Format(UiText.Get("Benchmark.Prompt.Running", "Measuring {0}..."), running ?? string.Empty),
        RunState.Finished => UiText.Get("Benchmark.Prompt.Finished", "Finished. Enter runs it again."),
        RunState.Cancelled => UiText.Get("Benchmark.Prompt.Cancelled", "Cancelled; the scores so far are shown. Enter runs it again."),
        _ => UiText.Get("Benchmark.Prompt.Idle", "Enter starts the benchmark. It takes about half a minute."),
    };

    private static string ProbeName(IBenchmarkProbe probe) => UiText.Get("Benchmark.Probe." + probe.Id, probe.Name);

    private static string CategoryName(BenchmarkCategory category) => category switch
    {
        BenchmarkCategory.Cpu => UiText.Get("Benchmark.Category.Cpu", "CPU"),
        BenchmarkCategory.Memory => UiText.Get("Benchmark.Category.Memory", "Memory"),
        BenchmarkCategory.Disk => UiText.Get("Benchmark.Category.Disk", "Disk"),
        BenchmarkCategory.Network => UiText.Get("Benchmark.Category.Network", "Network"),
        _ => UiText.Get("Benchmark.Category.Latency", "Latency"),
    };

    private static string ScoreText(double? score) => score is { } value ? value.ToString("N0") : "-";

    private static string FormatValue(double value) => value >= 100 ? value.ToString("N0") : value >= 10 ? value.ToString("N1") : value.ToString("N2");

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int innerWidth = (int)ViewSize.Width - 2;
        if (innerWidth <= 0 || ViewSize.Height < 3)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        List<(string Text, Color Color)> rows = Rows(innerWidth, colorScheme);
        // Stops above the hint row, so a short screen cuts the list and never the hint.
        for (int i = 0; i < rows.Count && i + 1 < ViewSize.Height - 2; i++)
        {
            WriteRow(buffer, colorScheme, innerWidth, i + 1, rows[i].Text, rows[i].Color);
        }

        string hint = IsRunning
            ? UiText.Get("Benchmark.Hint.Footer.Running", "Esc:cancel the run")
            : UiText.Get("Benchmark.Hint.Footer", "Enter:run  Esc:close");
        WriteRow(buffer, colorScheme, innerWidth, (int)ViewSize.Height - 2, hint.Length <= innerWidth ? hint : hint.Cut(innerWidth), colorScheme.DisabledForeground);
    }

    private void WriteRow(IBuffer buffer, ColorScheme colorScheme, int innerWidth, int row, string text, Color color)
    {
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + row),
            new Size(innerWidth, 1),
            new ConxelString(text.ExpandRight(innerWidth), color, colorScheme.Background));
    }

    /// <summary>Hands each progress report to the callback on the thread that reports it, so the order of starts and results is kept.</summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
