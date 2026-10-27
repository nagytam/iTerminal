using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Diagnostics;
using iConsole.Drivers;
using iConsole.Helper;
using iConsole.Jobs;
using iConsole.Screens;
using Microsoft.Extensions.Hosting;

namespace iConsole.Desktops;

public class Desktop : IHostedService, IDesktop
{
    private IConsoleDriver _consoleDriver { get; set; }
    public ConcurrentList<IScreen> Screens { get; private set; } = [];
    public ConcurrentList<IJob> Jobs { get; private set; } = [];
    private readonly List<Task> _jobTasks = [];
    private Task? _runTask;
    // Raise the Windows timer resolution to 1 ms so the render/input loops' Thread.Sleep(1)
    // is honoured (default ~15.6 ms), keeping navigation, screen refresh and the clock responsive.
    private TimerResolution? _timerResolution;
    public IBuffer Buffer { get; private set; }
    public CancellationTokenSource CancellationTokenSource { get; private set; } = new();
    public bool IsActive => !CancellationTokenSource.Token.IsCancellationRequested;
    public IRenderingDumpHelper Dump { get; }
    public IScreenLogger ScreenLogger { get; }
    private readonly IHostApplicationLifetime? _applicationLifetime;

    public Desktop(IConsoleDriver consoleDriver, IScreen screen, IBuffer buffer,
        IEnumerable<IJob> jobs,
        IRenderingDumpHelper? dump = null,
        ICommandLogger? commandLogger = null,
        IScreenLogger? screenLogger = null,
        IHostApplicationLifetime? applicationLifetime = null)
    {
        Dump = dump ?? new RenderingDumpHelper();
        ScreenLogger = screenLogger ?? NullScreenLogger.Instance;
        _consoleDriver = consoleDriver;
        Buffer = buffer;
        _applicationLifetime = applicationLifetime;
        foreach (var job in jobs)
        {
            AddJob(job);
        }

        if (Jobs.FirstOrDefault(j => j is ConsoleBufferRendererJob) is ConsoleBufferRendererJob consoleBufferRendererJob)
        {
            consoleBufferRendererJob.WindowSizeChanged += OnWindowSizeChanged;
        }
        screen.Desktop = this;
        if (screen is Control screenControl)
        {
            screenControl.Dump = Dump;
            screenControl.SetCommandLogger(commandLogger ?? NullCommandLogger.Instance);
        }
        Screens.Add(screen);
    }

    protected void AddJob(IJob job)
    {
        job.OnInitialize(this);
        Jobs.Add(job);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timerResolution = new TimerResolution(1);
        _runTask = Task.Run(RunAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!CancellationTokenSource.IsCancellationRequested)
        {
            CancellationTokenSource.Cancel();
        }

        foreach (var screen in Screens)
        {
            screen.Close();
        }

        try
        {
            Task.WaitAll([.. _jobTasks], cancellationToken);
            _runTask?.Wait(cancellationToken);
        }
        catch
        {
        }

        foreach (var job in Jobs)
        {
            job.OnAfterStop();
        }

        _consoleDriver.Clear();

        _timerResolution?.Dispose();
        _timerResolution = null;

        return Task.CompletedTask;
    }

    public async Task RunAsync()
    {
        try
        {
            Dump.AppendSessionTrace("desktop:run:start");
            _consoleDriver.RefreshWindowSize();
            var initialWindowSize = _consoleDriver.WindowSize;
            Dump.AppendSessionTrace($"desktop:run:window-size={initialWindowSize.Width}x{initialWindowSize.Height}");
            Buffer.Resize(initialWindowSize);
            Dump.InitializeSession(_consoleDriver);
            Dump.AppendSessionTrace("desktop:run:session-initialized");

            foreach (var screen in Screens.AsSnapshot())
            {
                Dump.AppendSessionTrace($"desktop:run:screen-initialize:start;screen={screen.GetType().FullName}");
                await screen.OnInitializeAsync();
                Dump.AppendSessionTrace($"desktop:run:screen-initialize:done;screen={screen.GetType().FullName}");
                screen.OnWindowSizeChanged(this, initialWindowSize);
                Dump.AppendSessionTrace($"desktop:run:screen-window-size:done;screen={screen.GetType().FullName}");
            }

            Dump.AppendSessionTrace("desktop:run:render-initial-state:start");
            RenderInitialState();
            Dump.AppendSessionTrace("desktop:run:render-initial-state:done");

            foreach (var job in Jobs.AsSnapshot())
            {
                StartJob(job);
            }

            Dump.AppendSessionTrace("desktop:run:loop:start");
            while (!CancellationTokenSource.IsCancellationRequested)
            {
                Thread.Sleep(10);
            }

            // The desktop token was cancelled (e.g. F10 Quit cancels it via QuitCommand).
            // Cancelling the token only stops the desktop loop and its jobs; the generic host
            // keeps running because host.RunAsync() awaits the application lifetime. Request a
            // host shutdown so the process actually exits. Harmless if the host is already
            // stopping (the Ctrl+C / StopAsync path), as StopApplication is idempotent.
            Dump.AppendSessionTrace("desktop:run:loop:cancelled;requesting-host-stop");
            _applicationLifetime?.StopApplication();
        }
        catch (Exception exception)
        {
            Dump.AppendError(exception);
            Dump.AppendSessionTrace($"desktop:run:error;type={exception.GetType().FullName};message={exception.Message}");
            throw;
        }
    }

    private void StartJob(IJob job)
    {
        Dump.AppendSessionTrace($"desktop:run:job-start;job={job.GetType().FullName};priority={job.Priority}");

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread jobThread = new(() =>
        {
            try
            {
                job.ExecuteAsync(CancellationTokenSource.Token).GetAwaiter().GetResult();
                completion.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = job.GetType().FullName,
            Priority = MapThreadPriority(job.Priority),
        };

        _jobTasks.Add(completion.Task);
        jobThread.Start();
    }

    private static ThreadPriority MapThreadPriority(JobPriority priority)
    {
        // Rendering must have strictly higher thread priority than all other background tasks.
        // Map input to AboveNormal so rendering remains Highest.
        return priority switch
        {
            JobPriority.Rendering => ThreadPriority.Highest,
            JobPriority.Input => ThreadPriority.AboveNormal,
            JobPriority.Background => ThreadPriority.BelowNormal,
            _ => ThreadPriority.Normal,
        };
    }

    private void OnWindowSizeChanged(object? sender, Size e)
    {
        Screens.First().OnWindowSizeChanged(sender, e);
        RenderInitialState();
    }

    private void RenderInitialState()
    {
        lock (Buffer.SyncLock)
        {
            foreach (var screen in Screens.AsSnapshot())
            {
                Dump.AppendSessionTrace($"desktop:initial-state:screen:start;screen={screen.GetType().FullName}");
                var maxZOrder = screen.GetMaxZOrder();
                Dump.AppendSessionTrace($"desktop:initial-state:screen:max-z={maxZOrder}");
                Buffer.UpdateLayers(maxZOrder);
                Dump.AppendSessionTrace($"desktop:initial-state:screen:layers-updated;count={Buffer.Layers.Count}");
                screen.Render(Buffer);
                Dump.AppendSessionTrace($"desktop:initial-state:screen:rendered;screen={screen.GetType().FullName}");
                if (screen.IsCursorVisible)
                {
                    Buffer.SetCursorPosition(screen.CursorPosition!, true);
                    Dump.AppendSessionTrace($"desktop:initial-state:screen:cursor;position={screen.CursorPosition?.X},{screen.CursorPosition?.Y}");
                }
            }

            Dump.AppendSessionTrace("desktop:initial-state:merge:start");
            Buffer.Merge();
            Dump.AppendSessionTrace("desktop:initial-state:merge:done");
            ScreenLogger.Capture(
                "desktop:initial-state",
                Buffer.MergedLayer,
                Screens.AsSnapshot().Select(s => s.GetType().FullName ?? "<null>").ToArray());
            var initialDisplayValidation = ConsoleBufferRendererJob.CreateDisplayValidationSummary(
                Buffer.MergedLayer,
                ["initial-state"],
                []);

            Dump.AppendSessionTrace("desktop:initial-state:dump-buffer:start");
            Dump.DumpBufferStage(
                "buffer-initial-state",
                Buffer.MergedLayer,
                [
                    $"screens={Screens.Count}",
                    $"cursor-visible={Buffer.Cursor.IsCursorVisible}",
                    $"cursor-position={Buffer.Cursor.CursorPosition?.X},{Buffer.Cursor.CursorPosition?.Y}"
                ]);
            Dump.AppendSessionTrace("desktop:initial-state:dump-buffer:done");
            Dump.DumpTextArtifact("buffer-initial-state-display-validation.txt", initialDisplayValidation);
            Dump.AppendSessionTrace("desktop:initial-state:dump-validation:done");
            Dump.DumpTextArtifact("console-window-state.txt", RenderingDumpHelper.CreateWindowSummary(_consoleDriver));
            Dump.AppendSessionTrace("desktop:initial-state:dump-window:done");
            Dump.AppendSessionTrace(
                $"desktop:initial-state;non-space={RenderingDumpHelper.CountVisibleCharacters(Buffer.MergedLayer)};display-validation=placeholder-empty-display");
        }
    }

    /// <summary>
    /// The control every key event is routed to: the topmost modal on the first screen, or the
    /// screen itself when nothing is modal. "Topmost" is the last one in the child list — that is
    /// the order the controls are drawn in, so it is the one the user can see. See
    /// <see cref="Control.GetModalChildControl"/> for why reading the first one instead sent Escape
    /// to the dialog *behind* the one on screen.
    /// </summary>
    public IControl? GetModalChildControl()
    {
        var screen = Screens.AsSnapshot().First();
        foreach (var control in screen.Controls.AsSnapshot().Reverse())
        {
            var modalControl = control.GetModalChildControl();
            if (modalControl is not null)
            {
                return modalControl;
            }
            if (control.IsModal)
            {
                return control;
            }
        }

        return screen;
    }

    public virtual void OnIdle()
    {
        DisableCursorIfHidden();
        foreach (var screen in Screens)
        {
            try
            {
                screen.OnIdle();
            }
            catch
            {
            }
        }
    }

    private void DisableCursorIfHidden()
    {
        var isCursorVisible = false;
        var shape = Display.CursorShape.Default;
        foreach (var screen in Screens.AsSnapshot())
        {
            foreach (var control in screen.Controls.AsSnapshot())
            {
                if (control.IsCursorVisible)
                {
                    isCursorVisible = true;
                    shape = control.EffectiveCursorShape;
                    break;
                }
            }
        }

        Buffer.Cursor.IsCursorVisible = isCursorVisible;
        Buffer.Cursor.Shape = shape;
    }
}
