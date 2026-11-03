namespace iTerminal.Core.Jobs;

/// <summary>
/// How long the always-on jobs that reach the network wait after they start before their first refresh. The cached
/// value is shown at once; the fetch (a web request, or a headless browser for the quotes) would otherwise compete
/// with the first keys the user presses.
/// </summary>
public static class StartupDelays
{
    public static readonly TimeSpan FirstNetworkRefresh = TimeSpan.FromSeconds(5);
}
