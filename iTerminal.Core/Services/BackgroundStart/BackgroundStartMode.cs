namespace iTerminal.Core.Services.BackgroundStart;

/// <summary>When a configurable background service starts its first pass.</summary>
public enum BackgroundStartMode
{
    /// <summary>At start-up, with no wait.</summary>
    Immediately,

    /// <summary>After <see cref="BackgroundStartService.DelayedStart"/>, or as soon as a feature that uses it asks.</summary>
    Delayed,

    /// <summary>Only when a feature that uses it asks — opening the dialog or panel is what starts it.</summary>
    OnDemand,
}
