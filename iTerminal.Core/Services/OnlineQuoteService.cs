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
    /// Indicates whether a refresh is currently in progress
    /// </summary>
    public bool IsRefreshing { get; set; } = false;
}
