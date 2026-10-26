using System.Text;

namespace iConsole.Display;

/// <summary>
/// Hands out the small ids a <see cref="Conxel"/> carries in place of a link target. A cell is compared and copied as raw
/// bytes, so it cannot hold a string; it holds the id and the renderer looks the target up here when it writes an OSC 8
/// link. Equal targets share one id, so a link that is redrawn every frame does not use up the table.
/// </summary>
public static class Hyperlinks
{
    /// <summary>The id of "no link"; every other id is a target.</summary>
    public const ushort None = 0;

    /// <summary>Longest target kept. Terminals cap a link at about this length and ignore longer ones.</summary>
    public const int MaxTargetLength = 2083;

    private static readonly Lock s_lock = new();
    private static readonly Dictionary<string, ushort> s_ids = new(StringComparer.Ordinal);
    private static readonly List<string> s_targets = [string.Empty];

    /// <summary>
    /// The id for <paramref name="target"/>, or <see cref="None"/> when it is empty, too long, or the table is full.
    /// Characters outside printable ASCII are percent-encoded: a control character would end the escape sequence early,
    /// which lets text pick what the terminal does next.
    /// </summary>
    public static ushort Register(string? target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return None;
        }

        string safe = Sanitize(target);
        if (safe.Length > MaxTargetLength)
        {
            return None;
        }

        lock (s_lock)
        {
            if (s_ids.TryGetValue(safe, out ushort id))
            {
                return id;
            }

            if (s_targets.Count > ushort.MaxValue)
            {
                return None;
            }

            id = (ushort)s_targets.Count;
            s_targets.Add(safe);
            s_ids[safe] = id;
            return id;
        }
    }

    /// <summary>The target behind <paramref name="id"/>, or null for <see cref="None"/> and an id never handed out.</summary>
    public static string? Resolve(ushort id)
    {
        if (id == None)
        {
            return null;
        }

        lock (s_lock)
        {
            return id < s_targets.Count ? s_targets[id] : null;
        }
    }

    internal static string Sanitize(string? target)
    {
        target ??= string.Empty;
        bool clean = true;
        foreach (char c in target)
        {
            if (c < 0x21 || c > 0x7E)
            {
                clean = false;
                break;
            }
        }

        if (clean)
        {
            return target;
        }

        StringBuilder builder = new(target.Length + 16);
        foreach (byte b in Encoding.UTF8.GetBytes(target))
        {
            if (b < 0x21 || b > 0x7E)
            {
                builder.Append('%').Append(b.ToString("X2"));
            }
            else
            {
                builder.Append((char)b);
            }
        }

        return builder.ToString();
    }
}
