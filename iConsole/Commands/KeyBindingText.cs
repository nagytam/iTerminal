using iConsole.Data;

namespace iConsole.Commands;

/// <summary>
/// Reads a key the way the help screen prints it (<c>Ctrl+Shift+F5</c>, <c>Alt+F1</c>, <c>Esc</c>) back into a
/// <see cref="Key"/>. Modifiers are <c>Ctrl</c>, <c>Shift</c> and <c>Alt</c>, in any order, and the key is last.
/// The key is a <see cref="ConsoleKey"/> name (<c>F5</c>, <c>PageDown</c>, <c>NumPad7</c>), a letter or a digit, or one
/// of a few short names (<c>Esc</c>, <c>Del</c>, <c>Ins</c>, <c>Space</c>, <c>PgUp</c>, <c>PgDn</c>, <c>Up</c>...).
/// </summary>
public static class KeyBindingText
{
    private static readonly Dictionary<string, ConsoleKey> ShortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = ConsoleKey.Escape,
        ["Del"] = ConsoleKey.Delete,
        ["Ins"] = ConsoleKey.Insert,
        ["Space"] = ConsoleKey.Spacebar,
        ["Menu"] = ConsoleKey.Applications,
        ["PgUp"] = ConsoleKey.PageUp,
        ["PgDn"] = ConsoleKey.PageDown,
        ["Up"] = ConsoleKey.UpArrow,
        ["Down"] = ConsoleKey.DownArrow,
        ["Left"] = ConsoleKey.LeftArrow,
        ["Right"] = ConsoleKey.RightArrow,
        ["Num+"] = ConsoleKey.Add,
        ["Num-"] = ConsoleKey.Subtract,
        ["Num*"] = ConsoleKey.Multiply,
        ["Num/"] = ConsoleKey.Divide,
        [";"] = ConsoleKey.Oem1,
        ["/"] = ConsoleKey.Oem2,
        ["`"] = ConsoleKey.Oem3,
        ["["] = ConsoleKey.Oem4,
        ["\\"] = ConsoleKey.Oem5,
        ["]"] = ConsoleKey.Oem6,
        ["'"] = ConsoleKey.Oem7,
        ["="] = ConsoleKey.OemPlus,
        ["-"] = ConsoleKey.OemMinus,
        [","] = ConsoleKey.OemComma,
        ["."] = ConsoleKey.OemPeriod,
    };

    /// <summary>Parses <paramref name="text"/>. On failure <paramref name="error"/> says what is wrong with it.</summary>
    public static bool TryParse(string? text, out Key? key, out string? error)
    {
        key = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "the key is empty";
            return false;
        }

        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string rest = text.Trim();
        while (rest.IndexOf('+') is var plus and >= 0 && IsModifierName(rest[..plus].Trim()))
        {
            // A '+' that does not follow a modifier is part of the key's own name ("Num+"), so it ends the scan.
            string modifier = rest[..plus].Trim();
            if (!modifiers.Add(modifier))
            {
                error = $"'{modifier}' is given twice";
                return false;
            }

            rest = rest[(plus + 1)..];
        }

        string name = rest.Trim();
        if (name.Length == 0)
        {
            error = "no key follows the modifiers";
            return false;
        }

        if (!TryConsoleKey(name, out ConsoleKey consoleKey))
        {
            error = $"'{name}' is not a key";
            return false;
        }

        // The set compares case-insensitively, so "ctrl" and "CTRL" count as Ctrl too.
        bool control = modifiers.Contains("Ctrl") || modifiers.Contains("Control");
        bool shift = modifiers.Contains("Shift");
        bool alt = modifiers.Contains("Alt");
        key = new Key(consoleKey, shift, alt, control);
        return true;
    }

    private static bool IsModifierName(string text) =>
        text.ToUpperInvariant() is "CTRL" or "CONTROL" or "SHIFT" or "ALT";

    private static bool TryConsoleKey(string name, out ConsoleKey consoleKey)
    {
        if (ShortNames.TryGetValue(name, out consoleKey))
        {
            return true;
        }

        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            return Enum.TryParse(char.ToUpperInvariant(name[0]).ToString(), out consoleKey);
        }

        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            consoleKey = ConsoleKey.D0 + (name[0] - '0');
            return true;
        }

        // A number is not a key name: "13" would parse as Enter, which the help screen never prints.
        if (name.All(char.IsAsciiDigit))
        {
            return false;
        }

        // IsDefined keeps a number such as "300" from parsing into a ConsoleKey that does not exist.
        return Enum.TryParse(name, ignoreCase: true, out consoleKey) && Enum.IsDefined(consoleKey);
    }
}
