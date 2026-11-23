using iConsole.Data;

namespace iConsole.Services;

/// <summary>
/// The log files the F3 viewer has shown lately, newest first, so keys can step from one to another. The viewer
/// records a log when it opens one; the app keeps the list and decides how many it holds.
/// </summary>
public interface ILogFileHistory
{
    /// <summary>Remembers <paramref name="url"/> as the newest log. An earlier entry for the same file moves up to the front.</summary>
    void Record(Url url);

    /// <summary>The log that was shown before <paramref name="current"/> (an older entry), or null when there is none.</summary>
    Url? Older(Url current);

    /// <summary>The log that was shown after <paramref name="current"/> (a newer entry), or null when there is none.</summary>
    Url? Newer(Url current);
}
