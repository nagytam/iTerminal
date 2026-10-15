using iConsole.Helper;
using iConsole.Tasks;
using System.ComponentModel;

namespace iTerminal.Core.Tasks;

/// <summary>
/// One entry of a <see cref="BatchOperationTask"/>: the task that copies, moves or deletes it, and
/// the discovery task that feeds it when it is a folder walked across drives.
/// </summary>
/// <param name="Label">The entry's name, for the progress text and the list of failures.</param>
public sealed record BatchStep(string Label, BackgroundTask Primary, BackgroundTask? Discovery = null);

/// <summary>
/// F5, Shift+F6 or F8 over marked rows: the single-entry tasks, run one after the other under one
/// progress window.
///
/// <para>One task rather than one per entry because that is what the user asked for — one
/// operation — and what the progress window, the panel refresh and Cancel all hang off. The steps
/// keep their own pipelines: a folder's discovery and transfer still run side by side, exactly as
/// they do for a single folder.</para>
///
/// <para>A step that fails does not stop the rest — one file already at the target should not cost
/// the other forty — and the failures are listed at the end, which keeps the window up
/// (<see cref="BackgroundTask.Error"/>) so they can be read. Cancel stops the step that is running
/// and every step after it.</para>
/// </summary>
public sealed class BatchOperationTask : BackgroundTask
{
    private readonly string _verb;
    private readonly IReadOnlyList<BatchStep> _steps;
    private readonly Func<Task>? _prepareAsync;

    /// <param name="verb">"Copy", "Move", "Delete" — the first word of every progress line.</param>
    /// <param name="prepareAsync">Runs before the first step, on the task's own thread — creating the target folder, say.</param>
    public BatchOperationTask(string verb, IReadOnlyList<BatchStep> steps, Func<Task>? prepareAsync = null)
    {
        _verb = verb;
        _steps = steps;
        _prepareAsync = prepareAsync;
        Name = $"{verb} {steps.Count} item{(steps.Count == 1 ? string.Empty : "s")}";
        Description = $"{Name}{Environment.NewLine}started";
    }

    public IReadOnlyList<BatchStep> Steps => _steps;

    /// <summary>The steps that finished without an error.</summary>
    public int Succeeded { get; private set; }

    /// <summary>"name: why" for every step that failed.</summary>
    public IReadOnlyList<string> Failures { get; private set; } = [];

    public override async Task ExecuteAsync()
    {
        var failures = new List<string>();
        try
        {
            if (_prepareAsync is not null)
            {
                await _prepareAsync();
            }
        }
        catch (Exception exception)
        {
            Description = $"{_verb} failed: {exception.Message}";
            Error = exception.Message;
            return;
        }

        for (var index = 0; index < _steps.Count && !CancellationToken.IsCancellationRequested; index++)
        {
            var step = _steps[index];
            var position = index;
            Description = $"{_verb} {position + 1} of {_steps.Count}{Environment.NewLine}{step.Label.Shrink(60)}";

            void OnStepChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(ProgressPercentage) or nameof(Description))
                {
                    var stepPercentage = Math.Clamp(step.Primary.ProgressPercentage, 0, 100);
                    OnProgressChanged((position * 100 + stepPercentage) / _steps.Count);
                    Description = $"{_verb} {position + 1} of {_steps.Count}{Environment.NewLine}{step.Primary.Description}";
                }
            }

            step.Primary.PropertyChanged += OnStepChanged;
            using var cancelStep = CancellationToken.Register(() =>
            {
                step.Primary.CancellationTokenSource.Cancel();
                step.Discovery?.CancellationTokenSource.Cancel();
            });
            try
            {
                // On their own threads, as the task manager would run them: a discovery task
                // writes into a bounded channel that only the primary drains.
                var primary = Task.Run(step.Primary.RunAsync);
                if (step.Discovery is not null)
                {
                    await Task.WhenAll(Task.Run(step.Discovery.RunAsync), primary);
                }
                else
                {
                    await primary;
                }

                if (step.Primary.Error is not null)
                {
                    failures.Add($"{step.Label}: {step.Primary.Error}");
                }
                else if (!CancellationToken.IsCancellationRequested)
                {
                    Succeeded++;
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{step.Label}: {exception.Message}");
            }
            finally
            {
                step.Primary.PropertyChanged -= OnStepChanged;
            }
        }

        Failures = failures;
        if (CancellationToken.IsCancellationRequested)
        {
            Description = $"{_verb} cancelled after {Succeeded} of {_steps.Count} item(s)";
            return;
        }

        ProgressPercentage = 100;
        if (failures.Count == 0)
        {
            Description = $"{_verb} finished: {Succeeded} item(s)";
            return;
        }

        Error = $"{failures.Count} of {_steps.Count} item(s) failed.";
        Description = $"{_verb} finished: {Succeeded} of {_steps.Count} item(s)"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures);
    }
}
