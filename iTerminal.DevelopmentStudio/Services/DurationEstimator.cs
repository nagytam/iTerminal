using System.Text.Json;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>
/// How long the build, clean and test commands took, kept in the settings so the studio can say how long the next run should
/// take. A target's estimate is the time of its own runs: the first measurement is taken as it is, and each later one moves the
/// estimate by <see cref="Smoothing"/> of the difference, so the recent runs count most and an old slow run fades. A target
/// that has never been measured falls back to the estimate of the runs of its language and kind.
/// </summary>
public sealed class DurationEstimator
{
    /// <summary>The share of the difference a new measurement moves the estimate by.</summary>
    public const double Smoothing = 0.3;

    /// <summary>The most entries kept; the least recently measured are dropped past this.</summary>
    public const int MaxEntries = 300;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>One estimate: its seconds, how many runs it was made from, and when it was last measured (UTC ticks).</summary>
    public sealed record Entry(double Seconds, int Samples, long Updated);

    /// <summary>The number of entries held.</summary>
    public int Count => _entries.Count;

    /// <summary>The estimate for the command on the item, or null when no run of it, or of its kind, was measured.</summary>
    public TimeSpan? Estimate(string verb, DevelopmentProject item)
    {
        if (_entries.TryGetValue(PathKey(verb, item), out Entry? own)) return TimeSpan.FromSeconds(own.Seconds);
        if (_entries.TryGetValue(GroupKey(verb, item), out Entry? group)) return TimeSpan.FromSeconds(group.Seconds);
        return null;
    }

    /// <summary>Adds one measured run of the command on the item to its estimate and to the estimate of its kind.</summary>
    public void Record(string verb, DevelopmentProject item, TimeSpan measured, long utcTicks)
    {
        double seconds = Math.Max(0, measured.TotalSeconds);
        Add(PathKey(verb, item), seconds, utcTicks);
        Add(GroupKey(verb, item), seconds, utcTicks);
        Prune();
    }

    /// <summary>The estimates as JSON, the form the settings keep them in.</summary>
    public string Encode() => JsonSerializer.Serialize(_entries);

    /// <summary>
    /// The estimates a stored value holds. A missing value gives none, and so does a damaged one: the estimates are relearned
    /// from the next runs rather than blocking the studio.
    /// </summary>
    public static DurationEstimator Decode(string? text)
    {
        var estimator = new DurationEstimator();
        if (string.IsNullOrWhiteSpace(text)) return estimator;
        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, Entry>>(text);
            if (stored is null) return estimator;
            foreach (var (key, entry) in stored)
            {
                if (double.IsFinite(entry.Seconds) && entry.Seconds >= 0 && entry.Samples > 0) estimator._entries[key] = entry;
            }

            estimator.Prune();
        }
        catch (JsonException)
        {
            return new DurationEstimator();
        }

        return estimator;
    }

    /// <summary>The key of the item's own estimate for a command.</summary>
    public static string PathKey(string verb, DevelopmentProject item) => verb + "|" + item.Path;

    /// <summary>The key of the estimate shared by the items of one language and kind.</summary>
    public static string GroupKey(string verb, DevelopmentProject item) => verb + "|" + item.Language + "|" + item.Kind;

    private void Add(string key, double seconds, long utcTicks)
    {
        if (_entries.TryGetValue(key, out Entry? previous))
        {
            double smoothed = previous.Seconds + Smoothing * (seconds - previous.Seconds);
            _entries[key] = new Entry(smoothed, previous.Samples + 1, utcTicks);
        }
        else
        {
            _entries[key] = new Entry(seconds, 1, utcTicks);
        }
    }

    private void Prune()
    {
        if (_entries.Count <= MaxEntries) return;
        foreach (string key in _entries.OrderBy(pair => pair.Value.Updated).Take(_entries.Count - MaxEntries).Select(pair => pair.Key).ToList())
        {
            _entries.Remove(key);
        }
    }
}
