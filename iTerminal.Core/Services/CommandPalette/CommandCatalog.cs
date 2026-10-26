using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;

namespace iTerminal.Core.Services.CommandPalette;

/// <summary>One command as the palette lists it.</summary>
/// <param name="Key">The key it is bound to as the help screen writes it ("Alt+F5"), or empty.</param>
/// <param name="Description">What it does, or empty when that would only repeat the title.</param>
public sealed record PaletteEntry(ICommand Command, string Title, string Key, string Description);

/// <summary>
/// What Shift+F1 lists, and how a query narrows it.
///
/// <para>The commands are gathered from the controls on screen <em>at the moment the palette is
/// asked for</em>, while the panel still has focus — the panel's own commands answer
/// <c>CanExecute</c> by asking whether their panel is the focused one, and once the palette is up
/// nothing is. Hidden controls count: the hot key bar's Ctrl and Alt layers and every closed menu
/// are hidden, and they hold most of the app's features. What is left out is noise: commands that
/// cannot run now, and the plain keys that move a cursor or type a character — "Move down" is not
/// something anyone looks up.</para>
/// </summary>
public static class CommandCatalog
{
    /// <summary>Every command reachable from <paramref name="root"/> that can run now, each title-and-key once.</summary>
    public static IReadOnlyList<PaletteEntry> Collect(IControl root, Func<ICommand, bool>? exclude = null)
    {
        var entries = new List<PaletteEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(root, entries, seen, exclude, depth: 0);
        return entries;
    }

    private static void Walk(IControl control, List<PaletteEntry> entries, HashSet<string> seen, Func<ICommand, bool>? exclude, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        if (control is Control commandControl)
        {
            // Of several commands one control binds to the same key, only the first that can run
            // ever takes the key — so only that one is offered, as F1 help does.
            var keysTaken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var command in commandControl.Commands.AsSnapshot())
            {
                if (exclude?.Invoke(command) == true || string.IsNullOrWhiteSpace(command.Title) || IsNoise(command.Key))
                {
                    continue;
                }

                bool canExecute;
                try
                {
                    canExecute = command.CanExecute();
                }
                catch
                {
                    canExecute = false;
                }

                var key = command.KeyDisplayText ?? string.Empty;
                if (!canExecute || (key.Length > 0 && !keysTaken.Add(key)))
                {
                    continue;
                }

                if (seen.Add(command.Title.Trim() + "|" + key))
                {
                    var description = command.Description?.Trim() ?? string.Empty;
                    if (string.Equals(description.TrimEnd('.'), command.Title.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        description = string.Empty;
                    }

                    entries.Add(new PaletteEntry(command, command.Title.Trim(), key, description));
                }
            }
        }

        foreach (var child in control.Controls.AsSnapshot())
        {
            Walk(child, entries, seen, exclude, depth + 1);
        }
    }

    /// <summary>
    /// A plain key that moves a cursor, types or confirms — the commands behind those are a list's or
    /// a text box's, not features. Modified keys, function keys, Insert and the keypad's operators
    /// (marking) are kept.
    /// </summary>
    internal static bool IsNoise(Key? key)
    {
        if (key is null || key.Control || key.Alt)
        {
            return false;
        }

        var consoleKey = key.ConsoleKey;
        if (consoleKey is >= ConsoleKey.F1 and <= ConsoleKey.F24
            or ConsoleKey.Insert or ConsoleKey.Add or ConsoleKey.Subtract or ConsoleKey.Multiply)
        {
            return false;
        }

        if (key.Shift && consoleKey is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
        {
            // Shift+arrows mark rows: a feature, worth finding.
            return false;
        }

        return true;
    }

    /// <summary>
    /// The entries matching <paramref name="query"/>: every word of it has to appear in the title,
    /// the key or the description, in any order. Titles that start with the query come first, then
    /// titles holding every word, then the rest; with no query, the commands run most recently lead.
    /// </summary>
    public static IReadOnlyList<PaletteEntry> Filter(IReadOnlyList<PaletteEntry> entries, string? query, IReadOnlyList<string>? recent = null)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            var order = recent ?? [];
            return [.. entries
                .OrderBy(e => Recency(order, e))
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)];
        }

        var phrase = string.Join(' ', words);
        var letters = string.Concat(words);
        return [.. entries
            .Select(e => (Entry: e, Tier: Tier(e, words, phrase), Fuzzy: FuzzyScore(letters, e.Title)))
            .Where(x => x.Tier < 4 || x.Fuzzy > 0)
            .OrderBy(x => x.Tier)
            .ThenByDescending(x => x.Tier == 4 ? x.Fuzzy : 0)
            .ThenBy(x => Recency(recent ?? [], x.Entry))
            .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Entry)];
    }

    // 0-3: every word is a substring somewhere (title prefix first). 4: no substring match, but the letters
    // typed appear in the title in order ("cfld" finds "Compare folders"); ranked below the others.
    private static int Tier(PaletteEntry e, string[] words, string phrase)
    {
        if (!words.All(w => Contains(e.Title, w) || Contains(e.Key, w) || Contains(e.Description, w)))
        {
            return 4;
        }

        return e.Title.StartsWith(phrase, StringComparison.OrdinalIgnoreCase) ? 0
            : words.All(w => Contains(e.Title, w)) ? 1
            : words.All(w => Contains(e.Title, w) || Contains(e.Key, w)) ? 2
            : 3;
    }

    private static int FuzzyScore(string letters, string title) =>
        letters.Length >= 3 ? iConsole.Helper.FuzzyMatcher.Match(letters, title)?.Score ?? 0 : 0;

    private static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

    private static int Recency(IReadOnlyList<string> recent, PaletteEntry entry)
    {
        for (var index = 0; index < recent.Count; index++)
        {
            if (string.Equals(recent[index], entry.Title, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
