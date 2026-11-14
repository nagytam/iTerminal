using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.ScreenSavers;

/// <summary>
/// The screen saver. It sits last on the screen, so it is drawn over everything, and it is idle — hidden and not modal — until
/// the person has been away for the chosen time. Then it covers the screen and becomes the modal control: the next key press
/// or click only wakes the screen, and is not passed on.
///
/// <para>It is switched on from <see cref="OnIdle"/>, which runs on the render thread, so the control tree is only changed there
/// (and on the input thread when it is woken, as dialogs are closed). The animation runs on its own task and only asks for a
/// repaint. A terminal lent to a shell (Ctrl+O) is never covered, since the shell has to see the keys and draw its own screen.</para>
/// </summary>
public class ScreenSaverOverlay : Control
{
    private static readonly TimeSpan ReadInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    private readonly ISettingsService _settingsService;
    private readonly IUserActivityService _userActivityService;
    private readonly ITimeProvider _timeProvider;
    private readonly ConsoleOwnership? _ownership;
    private readonly Random _random = new();
    private readonly Lock _gate = new();

    private bool _isActive;
    private IScreenSaverEffect? _effect;
    private ScreenSaverCanvas? _canvas;
    private CancellationTokenSource? _animation;
    private long _nextReadMilliseconds;

    public ScreenSaverOverlay(
        IThemeService themeService,
        ISettingsService settingsService,
        IUserActivityService userActivityService,
        ITimeProvider timeProvider,
        ConsoleOwnership? ownership = null)
        : base(themeService)
    {
        _settingsService = settingsService;
        _userActivityService = userActivityService;
        _timeProvider = timeProvider;
        _ownership = ownership;
        // Hidden and not modal until it starts: an idle overlay must not take keys or be drawn.
        IsVisible = false;
        IsModal = false;
    }

    /// <summary>Whether the screen saver is covering the screen now.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _isActive;
            }
        }
    }

    /// <summary>Starts the screen saver if the settings ask for one and the person has been idle for long enough.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        TryStart();
    }

    /// <summary>Stops the screen saver, the same as a key press or a click would. Safe to call when it is not running.</summary>
    public void Wake() => Stop();

    private void TryStart()
    {
        long now = _timeProvider.MonotonicMilliseconds;
        lock (_gate)
        {
            // The settings are read twice a second, not at every idle pass.
            if (_isActive || now < _nextReadMilliseconds)
            {
                return;
            }

            _nextReadMilliseconds = now + (long)ReadInterval.TotalMilliseconds;
        }

        ScreenSaverKind kind = ScreenSaverSettings.ParseKind(_settingsService.Get(Settings.ScreenSaverEffect));
        if (kind == ScreenSaverKind.Off)
        {
            return;
        }

        int minutes = ScreenSaverSettings.ParseIdleMinutes(_settingsService.Get(Settings.ScreenSaverIdleMinutes));
        if (_userActivityService.IdleFor < TimeSpan.FromMinutes(minutes))
        {
            return;
        }

        if (_ownership?.IsLent == true)
        {
            return;
        }

        Start(kind);
    }

    private void Start(ScreenSaverKind kind)
    {
        IScreenSaverEffect? effect = CreateEffect(kind);
        if (effect is null)
        {
            return;
        }

        var size = GetRootControl().ViewSize;
        lock (_gate)
        {
            _effect = effect;
            _canvas = new ScreenSaverCanvas((int)size.Width, (int)size.Height);
            _animation = new CancellationTokenSource();
            _isActive = true;
        }

        Position = new Position(0, 0);
        ViewSize = size;
        IsModal = true;
        IsVisible = true;
        StateHasChanged();

        var token = _animation!.Token;
        Task.Run(() => AnimateAsync(token));
    }

    private void Stop()
    {
        CancellationTokenSource? animation;
        lock (_gate)
        {
            if (!_isActive)
            {
                return;
            }

            _isActive = false;
            animation = _animation;
            _animation = null;
            _effect = null;
            _canvas = null;
        }

        animation?.Cancel();
        IsModal = false;
        // Hide also repaints what the screen saver covered.
        Hide();
    }

    private async Task AnimateAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(FrameInterval, token);
                // A window in the background draws nothing; the frame is drawn when focus comes back.
                await TerminalFocus.Current.WaitForFocusAsync(token);
                if (!Step())
                {
                    return;
                }

                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            // Woken: nothing more to paint.
        }
    }

    /// <summary>Advances one frame at the size of the window. Returns false once the screen saver has stopped.</summary>
    private bool Step()
    {
        var size = GetRootControl().ViewSize;
        lock (_gate)
        {
            if (!_isActive || _effect is null)
            {
                return false;
            }

            if (ViewSize.Width != size.Width || ViewSize.Height != size.Height)
            {
                ViewSize = size;
            }

            if (_canvas is null || _canvas.Width != (int)size.Width || _canvas.Height != (int)size.Height)
            {
                _canvas = new ScreenSaverCanvas((int)size.Width, (int)size.Height);
            }

            _canvas.Clear();
            _effect.Step(_canvas, _timeProvider.Now);
            return true;
        }
    }

    /// <summary>The help overlay lists the one key that works: any key wakes the screen. Listed even while idle, as the guard checks every key handler.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Any key", iConsole.Localization.UiText.Get("ScreenSaver.Wake", "Wake the screen"));
    }

    public override Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!IsActive)
        {
            return Task.FromResult(false);
        }

        // Any key wakes the screen, and is used up doing so.
        Stop();
        return Task.FromResult(true);
    }

    public override Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (!IsActive)
        {
            return Task.FromResult(false);
        }

        // A pointer that only moves wakes nothing, but is still not passed on to the screen beneath.
        if (!mouseEvent.IsHover)
        {
            Stop();
        }

        return Task.FromResult(true);
    }

    public override void Draw(IBuffer buffer)
    {
        lock (_gate)
        {
            if (!_isActive || _canvas is null)
            {
                return;
            }

            var origin = AbsolutePosition;
            for (int y = 0; y < _canvas.Height; y++)
            {
                buffer.Write(ZOrder, new Position(origin.X, origin.Y + y), new Size(_canvas.Width, 1), _canvas.Row(y));
            }
        }
    }

    private IScreenSaverEffect? CreateEffect(ScreenSaverKind kind) => kind switch
    {
        ScreenSaverKind.Starfield => new StarfieldEffect(_random),
        ScreenSaverKind.MatrixRain => new MatrixRainEffect(_random),
        ScreenSaverKind.Clock => new ClockEffect(),
        _ => null,
    };
}
