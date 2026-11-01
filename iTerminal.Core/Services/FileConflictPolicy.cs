using System.Globalization;
using iConsole.Data;
using iConsole.Services;

namespace iTerminal.Core.Services;

/// <summary>
/// The rules behind each answer, as a pure function. Keep newer and Keep larger read the size and
/// date of local files only; for any other drive, or when a side is equal or unknown, the rule
/// returns <c>null</c>, which means "ask again" rather than guessing.
/// </summary>
public static class FileConflictPolicy
{
    public static FileConflictDecision? Decide(FileConflictChoice choice, Url sourceUrl, Url targetUrl, Func<Url, bool> exists)
    {
        switch (choice)
        {
            case FileConflictChoice.Overwrite:
                return new FileConflictDecision(Skip: false, targetUrl, Replace: true);
            case FileConflictChoice.Skip:
                return new FileConflictDecision(Skip: true, targetUrl, Replace: false);
            case FileConflictChoice.Rename:
                return new FileConflictDecision(Skip: false, RenamedTarget(targetUrl, exists), Replace: false);
            case FileConflictChoice.KeepNewer:
            {
                var source = ModifiedOf(sourceUrl);
                var target = ModifiedOf(targetUrl);
                if (source is null || target is null || source == target)
                {
                    return null;
                }
                return source > target
                    ? new FileConflictDecision(Skip: false, targetUrl, Replace: true)
                    : new FileConflictDecision(Skip: true, targetUrl, Replace: false);
            }
            case FileConflictChoice.KeepLarger:
            {
                var source = SizeOf(sourceUrl);
                var target = SizeOf(targetUrl);
                if (source is null || target is null || source == target)
                {
                    return null;
                }
                return source > target
                    ? new FileConflictDecision(Skip: false, targetUrl, Replace: true)
                    : new FileConflictDecision(Skip: true, targetUrl, Replace: false);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(choice), choice, "Cancel is not a rule; the resolver handles it.");
        }
    }

    /// <summary>Explorer-style free name beside the target: "a.txt" → "a (2).txt", "a (3).txt", ...</summary>
    public static Url RenamedTarget(Url targetUrl, Func<Url, bool> exists)
    {
        var name = targetUrl.Name;
        var parent = targetUrl.FullUrl[..(targetUrl.FullUrl.Length - name.Length)];
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var number = 2; ; number++)
        {
            var candidate = new Url(parent + $"{stem} ({number}){extension}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>The local file's last write time (to the minute, as the panels show it), or null when unknown.</summary>
    public static DateTime? ModifiedOf(Url url)
    {
        if (url.ContainerType != ContainerType.FileSystem)
        {
            return null;
        }
        var local = new Url(url.FullUrl);
        if (string.IsNullOrEmpty(local.Date) || string.IsNullOrEmpty(local.Time))
        {
            return null;
        }
        return DateTime.TryParseExact($"{local.Date} {local.Time}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>The local file's size in bytes, or null when unknown.</summary>
    public static long? SizeOf(Url url)
    {
        if (url.ContainerType != ContainerType.FileSystem)
        {
            return null;
        }
        return new Url(url.FullUrl).Length;
    }
}
