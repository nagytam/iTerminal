using iConsole.Data;
using iConsole.Services;

namespace iConsole.Controls.StatusBar;

public class Date : StatusBarItem, IDisposable
{
    // Increase interval to reduce cross-thread UI updates during tests and heavy IO operations.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IDateTimeProviderService _dateTimeProviderService;
    private readonly Timer _timer;
    private volatile bool _isDisposed;

    public Date(IThemeService themeService, IDateTimeProviderService dateTimeProviderService)
        : base(themeService)
    {
        _dateTimeProviderService = dateTimeProviderService;
        ViewSize = new Size(20, 1);

        _ = UpdateText(_dateTimeProviderService.Now);
        // Use Timer with dueTime = Timeout.Infinite to avoid firing during unit test construction
        // until explicitly started. Tests that need timer behavior can start it via Start().
        _timer = new Timer(OnTimerElapsed, null, Timeout.InfiniteTimeSpan, RefreshInterval);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        // Started here (production init path) rather than in the constructor so reflection/unit-test
        // construction does not spin up a live timer. Guarantees the clock advances every second
        // independently of the render-idle path.
        Start();
    }

    // Start the internal timer (tests can call this if they need live updates).
    public void Start()
    {
        try
        {
            _timer.Change(RefreshInterval, RefreshInterval);
        }
        catch
        {
        }
    }

    public override void OnIdle()
    {
        _ = UpdateText(_dateTimeProviderService.Now);
        base.OnIdle();
    }

    private void OnTimerElapsed(object? state)
    {
        if (_isDisposed)
        {
            return;
        }

        _ = UpdateText(_dateTimeProviderService.Now);
    }

    private bool UpdateText(DateTime now)
    {
        var text = $"{now:yyyy-MM-dd HH:mm:ss}";
        if (text != Text)
        {
            Text = text;
            StateHasChanged();
            return true;
        }

        return false;
    }

    #region IDisposable
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
    #endregion
}
