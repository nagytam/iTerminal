using iTerminal.Core.Data;
using System.Collections.Concurrent;

namespace iTerminal.Core.Services;

public class OnlineQuoteService
{
    public ConcurrentDictionary<string, Instrument> Instruments = [];

    /// <summary>
    /// Indicates whether the current instruments data is from cache (true) or freshly fetched (false)
    /// </summary>
    public bool IsFromCache { get; set; } = false;

    /// <summary>
    /// Whether the last attempt to read the quotes failed (no browser, a page that would not load). Without this a failed
    /// attempt looked like one still in progress: the indicator spun on with no sign that no quote was coming.
    /// </summary>
    public bool LastRefreshFailed { get; set; } = false;

    /// <summary>
    /// Indicates whether a refresh is currently in progress
    /// </summary>
    public bool IsRefreshing { get; set; } = false;
}
