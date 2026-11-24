using iConsole.Data;
using iConsole.Services;

namespace iTerminal.Core.Services;

/// <summary>
/// The last logs the F3 viewer showed, newest first, for Ctrl+PageUp and Ctrl+PageDown to step through. It holds at
/// most <see cref="Capacity"/> local files, read from the <see cref="SizeSettingKey"/> setting each time a log is
/// recorded, so a changed size applies from the next log. Only files that still exist are stepped to.
/// </summary>
public sealed class LogFileHistoryService(ISettingsService settingsService) : ILogFileHistory
{
    /// <summary>The setting that holds how many logs are kept; it is a number, and anything else means the default.</summary>
    public const string SizeSettingKey = "LogViewer.HistorySize";

    public const int DefaultCapacity = 10;
    public const int MinimumCapacity = 1;
    public const int MaximumCapacity = 100;

    private readonly Lock _gate = new();
    private readonly List<Url> _logs = [];

    /// <summary>How many logs are kept: the setting, clamped to 1..100, or 10 when it is missing or not a number.</summary>
    public int Capacity
    {
        get
        {
            string? text = settingsService.Get(SizeSettingKey);
            return int.TryParse(text, out int size)
                ? Math.Clamp(size, MinimumCapacity, MaximumCapacity)
                : DefaultCapacity;
        }
    }

    public void Record(Url url)
    {
        if (!IsLocalFile(url))
        {
            return;
        }

        int capacity = Capacity;
        lock (_gate)
        {
            _logs.RemoveAll(log => string.Equals(log.FullUrl, url.FullUrl, StringComparison.Ordinal));
            _logs.Insert(0, url);
            if (_logs.Count > capacity)
            {
                _logs.RemoveRange(capacity, _logs.Count - capacity);
            }
        }
    }

    public Url? Older(Url current) => Step(current, direction: 1);

    public Url? Newer(Url current) => Step(current, direction: -1);

    private Url? Step(Url current, int direction)
    {
        lock (_gate)
        {
            int index = _logs.FindIndex(log => string.Equals(log.FullUrl, current.FullUrl, StringComparison.Ordinal));
            if (index < 0)
            {
                return null;
            }

            for (int i = index + direction; i >= 0 && i < _logs.Count; i += direction)
            {
                if (File.Exists(_logs[i].Path))
                {
                    return _logs[i];
                }
            }

            return null;
        }
    }

    private static bool IsLocalFile(Url url) =>
        string.Equals(url.UrlScheme, Container.FILE, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(url.Path);
}
