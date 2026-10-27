namespace iConsole.Services;

/// <summary>
/// Whether the terminal window has the keyboard focus, as the terminal reports it (DEC private mode 1004, or the focus
/// records of the Windows console). Until a terminal reports anything the application counts as focused, so a terminal that
/// does not know the mode behaves as before. Polling loops ask <see cref="WaitForFocusAsync"/> before a pass and so stand
/// still while the window is in the background; a screen that was stale when focus came back is refreshed from
/// <see cref="FocusRegained"/>.
/// </summary>
public interface ITerminalFocusService
{
    /// <summary>False from a focus-out report until the next focus-in.</summary>
    bool HasFocus { get; }

    /// <summary>True once the terminal has reported focus at least once, so the reports can be relied on.</summary>
    bool IsReporting { get; }

    /// <summary>Raised with the new state whenever it changes, on the thread that read the report.</summary>
    event Action<bool>? FocusChanged;

    /// <summary>Raised when focus comes back after being lost.</summary>
    event Action? FocusRegained;

    /// <summary>Tells the service what the terminal reported. Reporting the state it is already in raises nothing.</summary>
    void Report(bool hasFocus);

    /// <summary>Completes at once while the window has focus, otherwise when it gets it back (or when cancelled).</summary>
    Task WaitForFocusAsync(CancellationToken cancellationToken = default);
}

public sealed class TerminalFocusService : ITerminalFocusService
{
    private readonly Lock _gate = new();
    private bool _hasFocus = true;
    private bool _isReporting;
    private TaskCompletionSource _regained = NewSource();

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool HasFocus
    {
        get { lock (_gate) { return _hasFocus; } }
    }

    public bool IsReporting
    {
        get { lock (_gate) { return _isReporting; } }
    }

    public event Action<bool>? FocusChanged;

    public event Action? FocusRegained;

    public void Report(bool hasFocus)
    {
        TaskCompletionSource? toComplete = null;
        lock (_gate)
        {
            _isReporting = true;
            if (_hasFocus == hasFocus)
            {
                return;
            }

            _hasFocus = hasFocus;
            if (hasFocus)
            {
                toComplete = _regained;
                _regained = NewSource();
            }
        }

        toComplete?.TrySetResult();
        FocusChanged?.Invoke(hasFocus);
        if (hasFocus)
        {
            FocusRegained?.Invoke();
        }
    }

    public Task WaitForFocusAsync(CancellationToken cancellationToken = default)
    {
        Task wait;
        lock (_gate)
        {
            if (_hasFocus)
            {
                return Task.CompletedTask;
            }

            wait = _regained.Task;
        }

        return wait.WaitAsync(cancellationToken);
    }
}

/// <summary>
/// The terminal's focus state for code that has no service to be given: controls and jobs in other assemblies read
/// <see cref="Current"/>. The application registers the same instance in the container, and a test replaces it.
/// </summary>
public static class TerminalFocus
{
    /// <summary>The instance in use; one that never loses focus until a terminal reports it.</summary>
    public static ITerminalFocusService Current { get; set; } = new TerminalFocusService();
}
