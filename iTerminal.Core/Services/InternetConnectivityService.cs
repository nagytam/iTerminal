namespace iTerminal.Core.Services;

/// <summary>
/// Whether the machine can reach the internet, as the menu bar's Online indicator shows it. Written by
/// <c>InternetConnectivityJob</c> and read on the UI thread, so the state is a single volatile value.
/// </summary>
public class InternetConnectivityService
{
    private const int Unknown = 0;
    private const int Online = 1;
    private const int Offline = 2;

    private volatile int _state = Unknown;

    /// <summary><c>null</c> until the first check has finished, then whether the last check reached the internet.</summary>
    public bool? IsOnline => _state switch
    {
        Online => true,
        Offline => false,
        _ => null,
    };

    /// <summary>Records the outcome of a check.</summary>
    public void Report(bool online) => _state = online ? Online : Offline;
}
