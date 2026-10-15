using iConsole.Controls;
using iConsole.Data;
using iConsole.Desktops;
using iConsole.Diagnostics;
using iConsole.Drivers;
using iConsole.Inputs;
using iConsole.Jobs;
using iConsole.Screens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Application;

namespace iConsole.Test.Core.Automation;

/// <summary>How a <see cref="ConsoleAppDriver"/> sets up the application it runs.</summary>
public sealed class ConsoleAppDriverOptions
{
    /// <summary>Terminal width in columns.</summary>
    public int Width { get; init; } = 120;

    /// <summary>Terminal height in rows.</summary>
    public int Height { get; init; } = 40;

    /// <summary>How long a wait gives the application before failing the test.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long the screen must stay unchanged to count as settled.</summary>
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Configuration handed to the application (what <c>appSettings.json</c> would hold).</summary>
    public IDictionary<string, string?> Configuration { get; init; } = new Dictionary<string, string?>();
}

/// <summary>
/// Runs an iConsole application in-process, on a <see cref="VirtualConsoleDriver"/> and a
/// <see cref="VirtualTerminalInput"/>, and drives it the way a person at a terminal would: press
/// keys, type, click, resize the window — and read back the screen.
///
/// <para>Everything between the keyboard and the screen is the production code: the generic host,
/// the <see cref="Desktop"/>, every registered job, the input routing to the topmost modal, and the
/// two render jobs including the diffing <c>ConsoleBufferRendererJob</c>. Only the two ends are
/// virtual. That is what separates these tests from the control-level ones: a dialog test proves the
/// dialog draws the right thing into a buffer; a driver test proves it reaches the screen, on top,
/// at the right place, after the key that opened it went through the real router.</para>
///
/// <para>Rendering is asynchronous by design (nothing draws from a key handler), so assertions go
/// through the <c>WaitFor…</c> methods, which poll the screen until a condition holds and fail with a
/// dump of the screen when it never does.</para>
/// </summary>
public sealed class ConsoleAppDriver : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly IHostApplicationLifetime _lifetime;
    private bool _isDisposed;

    private ConsoleAppDriver(IHost host, VirtualConsoleDriver console, VirtualTerminalInput input,
        InMemoryCommandLogger commandLogger, InMemoryScreenLogger screenLogger, ConsoleAppDriverOptions options)
    {
        _host = host;
        _lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Console = console;
        Input = input;
        CommandLogger = commandLogger;
        ScreenLogger = screenLogger;
        Options = options;
    }

    public VirtualConsoleDriver Console { get; }
    public VirtualTerminalInput Input { get; }
    public InMemoryCommandLogger CommandLogger { get; }
    public InMemoryScreenLogger ScreenLogger { get; }
    public ConsoleAppDriverOptions Options { get; }
    public IServiceProvider Services => _host.Services;
    public IDesktop Desktop => Services.GetRequiredService<IDesktop>();

    /// <summary>The screen as it is now.</summary>
    public ConsoleSnapshot Screen => Console.Snapshot();

    /// <summary>The control the next key goes to: the topmost modal, or the screen.</summary>
    public IControl? FocusedModal => Desktop.GetModalChildControl();

    /// <summary>True once the application has asked to shut down (F10, Ctrl+C, …).</summary>
    public bool HasExited => _lifetime.ApplicationStopping.IsCancellationRequested
        || Desktop.CancellationTokenSource.IsCancellationRequested;

    #region Start
    /// <summary>
    /// Starts an application whose screen is <typeparamref name="TScreen"/>. <paramref name="configureServices"/>
    /// registers the application's own services, as its <c>Program.cs</c> would; the console, the
    /// input, the iConsole core and the desktop are registered by the driver. Returns once the first
    /// frame is on screen.
    /// </summary>
    public static async Task<ConsoleAppDriver> StartAsync<TScreen>(
        Action<IServiceCollection, IConfiguration>? configureServices = null,
        ConsoleAppDriverOptions? options = null)
        where TScreen : Screen
    {
        options ??= new ConsoleAppDriverOptions();
        var console = new VirtualConsoleDriver(options.Width, options.Height);
        var input = new VirtualTerminalInput();
        var commandLogger = new InMemoryCommandLogger();
        var screenLogger = new InMemoryScreenLogger();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(options.Configuration)
            .Build();

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                // The host's default lifetime listens for Ctrl+C on the real console of the test
                // process; nothing here should.
                services.AddSingleton<IHostLifetime, DetachedHostLifetime>();
                services.AddSingleton<IConsoleDriver>(console);
                services.AddiConsoleCore();
                services.AddSingleton<ICommandLogger>(commandLogger);
                services.AddSingleton<IScreenLogger>(screenLogger);
                services.AddSingleton(input);
                services.AddSingleton<ITerminalInputSource>(input);
                services.AddSingleton<IJob, VirtualTerminalInputJob>();

                configureServices?.Invoke(services, configuration);

                // Last, as Program.cs does it: hosted services the application registered (database
                // initialisation, say) start before the desktop draws anything.
                services.AddSingleton<Desktop>();
                services.AddSingleton<IDesktop>(provider => provider.GetRequiredService<Desktop>());
                services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<Desktop>());
                services.AddSingleton<IScreen, TScreen>();
            })
            .Build();

        var driver = new ConsoleAppDriver(host, console, input, commandLogger, screenLogger, options);
        try
        {
            await host.StartAsync();
            await driver.WaitUntilAsync(screen => !screen.IsBlank, "the first frame to be drawn");
        }
        catch
        {
            await driver.DisposeAsync();
            throw;
        }

        return driver;
    }
    #endregion

    #region Input
    /// <summary>
    /// Presses <paramref name="chord"/> (see <see cref="KeyChord"/>) and waits until the application
    /// has handled it. Returns whether anything handled the key — an unhandled key beeps.
    /// </summary>
    public Task<bool> PressAsync(string chord) => PressAsync(KeyChord.Parse(chord));

    /// <summary>Sends <paramref name="keyEvent"/> and waits until the application has handled it.</summary>
    public async Task<bool> PressAsync(KeyEvent keyEvent)
    {
        ThrowIfExited();
        return await WithTimeout(Input.SendKeyAsync(keyEvent), $"the key {Describe(keyEvent)} to be handled");
    }

    /// <summary>Presses each chord in turn.</summary>
    public async Task PressSequenceAsync(params string[] chords)
    {
        foreach (var chord in chords)
        {
            await PressAsync(chord);
        }
    }

    /// <summary>Types <paramref name="text"/> one key at a time.</summary>
    public async Task TypeAsync(string text)
    {
        foreach (var keyEvent in KeyChord.FromText(text))
        {
            await PressAsync(keyEvent);
        }
    }

    /// <summary>Left-clicks the cell at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public async Task<bool> ClickAsync(int x, int y)
    {
        ThrowIfExited();
        return await WithTimeout(
            Input.SendMouseAsync(new MouseEvent { Position = new Position(x, y), LeftClicked = true }),
            $"the click at ({x},{y}) to be handled");
    }

    /// <summary>Left-clicks the first character of <paramref name="text"/>, waiting for it to appear.</summary>
    public async Task<bool> ClickAsync(string text)
    {
        var screen = await WaitForTextAsync(text);
        var position = screen.Find(text)!;
        return await ClickAsync((int)position.X, (int)position.Y);
    }

    /// <summary>Turns the mouse wheel over (<paramref name="x"/>, <paramref name="y"/>); +1 is up.</summary>
    public Task<bool> ScrollAsync(int x, int y, int delta) =>
        WithTimeout(
            Input.SendMouseAsync(new MouseEvent { Position = new Position(x, y), WheelDelta = delta }),
            $"the wheel at ({x},{y}) to be handled");

    /// <summary>Resizes the terminal and waits for the application to redraw at the new size.</summary>
    public async Task<ConsoleSnapshot> ResizeAsync(int width, int height)
    {
        Console.Resize(width, height);
        return await WaitUntilAsync(
            screen => screen.Width == width && screen.Height == height && !screen.IsBlank,
            $"the screen to be redrawn at {width}x{height}");
    }
    #endregion

    #region Waiting
    /// <summary>Waits until <paramref name="text"/> is on screen; returns the frame it was found in.</summary>
    public Task<ConsoleSnapshot> WaitForTextAsync(string text, TimeSpan? timeout = null) =>
        WaitUntilAsync(screen => screen.Contains(text), $"'{text}' to appear", timeout);

    /// <summary>Waits until <paramref name="text"/> is no longer on screen.</summary>
    public Task<ConsoleSnapshot> WaitForTextGoneAsync(string text, TimeSpan? timeout = null) =>
        WaitUntilAsync(screen => !screen.Contains(text), $"'{text}' to disappear", timeout);

    /// <summary>
    /// Polls the screen until <paramref name="condition"/> holds and returns that frame; fails the test
    /// with <paramref name="because"/> and a dump of the last frame when it never does.
    /// </summary>
    public async Task<ConsoleSnapshot> WaitUntilAsync(Func<ConsoleSnapshot, bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Options.Timeout);
        while (true)
        {
            var screen = Screen;
            if (condition(screen))
            {
                return screen;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new AssertFailedException($"Timed out waiting for {because}.{Environment.NewLine}{screen.Dump()}");
            }

            await Task.Delay(15);
        }
    }

    /// <summary>Polls a non-screen condition (a file, a model) with the same deadline and failure dump.</summary>
    public async Task WaitUntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        await WaitUntilAsync(_ => condition(), because, timeout);
    }

    /// <summary>
    /// Waits until the screen has stopped changing for <see cref="ConsoleAppDriverOptions.SettleTime"/> —
    /// for asserting that something did <i>not</i> happen, where there is no text to wait for.
    /// </summary>
    public async Task<ConsoleSnapshot> WaitForIdleAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Options.Timeout);
        var last = Screen;
        var stableSince = DateTime.UtcNow;
        while (true)
        {
            await Task.Delay(15);
            var screen = Screen;
            if (!screen.HasSameContent(last))
            {
                last = screen;
                stableSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - stableSince >= Options.SettleTime)
            {
                return screen;
            }

            if (DateTime.UtcNow > deadline)
            {
                // A screen that never settles (a running clock, a spinner) is still a screen.
                return screen;
            }
        }
    }

    /// <summary>Waits until the topmost modal is a <typeparamref name="TControl"/> and returns it.</summary>
    public async Task<TControl> WaitForModalAsync<TControl>(TimeSpan? timeout = null) where TControl : class, IControl
    {
        TControl? found = null;
        await WaitUntilAsync(() => (found = FocusedModal as TControl) is not null,
            $"a {typeof(TControl).Name} to be the topmost window", timeout);
        return found!;
    }

    /// <summary>Waits until the application has asked to shut down.</summary>
    public Task WaitForExitAsync(TimeSpan? timeout = null) =>
        WaitUntilAsync(() => HasExited, "the application to exit", timeout);
    #endregion

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _host.StopAsync(stopTimeout.Token);
        }
        catch
        {
            // A host that fails to stop cleanly must not hide the test's own result.
        }
        finally
        {
            _host.Dispose();
        }
    }

    private void ThrowIfExited()
    {
        if (HasExited)
        {
            throw new AssertFailedException($"The application has already exited.{Environment.NewLine}{Screen.Dump()}");
        }
    }

    private async Task<bool> WithTimeout(Task<bool> task, string because)
    {
        var completed = await Task.WhenAny(task, Task.Delay(Options.Timeout));
        if (completed != task)
        {
            throw new AssertFailedException(
                $"Timed out waiting for {because} — a key handler is blocking the input thread.{Environment.NewLine}{Screen.Dump()}");
        }

        return await task;
    }

    private static string Describe(KeyEvent keyEvent) =>
        (keyEvent.Control ? "Ctrl+" : string.Empty)
        + (keyEvent.Alt ? "Alt+" : string.Empty)
        + (keyEvent.Shift ? "Shift+" : string.Empty)
        + keyEvent.ConsoleKey;

    /// <summary>A host lifetime that neither waits for nor reacts to the real console.</summary>
    private sealed class DetachedHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
