using iConsole.Data;

namespace iConsole.Test.Core.Automation;

/// <summary>
/// Turns a readable chord — <c>"F5"</c>, <c>"Ctrl+G"</c>, <c>"Shift+Tab"</c>, <c>"Alt+F3"</c>,
/// <c>"a"</c>, <c>"Ctrl++"</c> — into the <see cref="KeyEvent"/> the platform input layers produce
/// for it, key character included (Enter is <c>'\r'</c>, Ctrl+letter its control code, Alt+letter
/// the letter itself — the panel's type-to-search reads that character).
///
/// <para>Modifiers are case-insensitive and may appear in any order; the key name is the last
/// segment. A single character is a key of its own, so <c>"A"</c> is Shift+A and <c>"+"</c> is the
/// plus key.</para>
/// </summary>
public static class KeyChord
{
    private static readonly Dictionary<string, (ConsoleKey Key, char Char)> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = (ConsoleKey.Enter, '\r'),
        ["Return"] = (ConsoleKey.Enter, '\r'),
        ["Esc"] = (ConsoleKey.Escape, '\u001b'),
        ["Escape"] = (ConsoleKey.Escape, '\u001b'),
        ["Tab"] = (ConsoleKey.Tab, '\t'),
        ["Space"] = (ConsoleKey.Spacebar, ' '),
        ["Spacebar"] = (ConsoleKey.Spacebar, ' '),
        ["Backspace"] = (ConsoleKey.Backspace, '\b'),
        ["Delete"] = (ConsoleKey.Delete, '\0'),
        ["Del"] = (ConsoleKey.Delete, '\0'),
        ["Insert"] = (ConsoleKey.Insert, '\0'),
        ["Ins"] = (ConsoleKey.Insert, '\0'),
        ["Home"] = (ConsoleKey.Home, '\0'),
        ["End"] = (ConsoleKey.End, '\0'),
        ["PageUp"] = (ConsoleKey.PageUp, '\0'),
        ["PgUp"] = (ConsoleKey.PageUp, '\0'),
        ["PageDown"] = (ConsoleKey.PageDown, '\0'),
        ["PgDn"] = (ConsoleKey.PageDown, '\0'),
        ["Up"] = (ConsoleKey.UpArrow, '\0'),
        ["Down"] = (ConsoleKey.DownArrow, '\0'),
        ["Left"] = (ConsoleKey.LeftArrow, '\0'),
        ["Right"] = (ConsoleKey.RightArrow, '\0'),

        // The numeric keypad's operator keys, which arrive as their own keys rather than as the
        // characters on the main keyboard. "Num+" cannot be written: '+' joins a chord.
        ["NumPlus"] = (ConsoleKey.Add, '+'),
        ["NumMinus"] = (ConsoleKey.Subtract, '-'),
        ["NumStar"] = (ConsoleKey.Multiply, '*'),
    };

    /// <summary>Parses <paramref name="chord"/>; throws <see cref="FormatException"/> for an unknown key.</summary>
    public static KeyEvent Parse(string chord)
    {
        ArgumentException.ThrowIfNullOrEmpty(chord);

        // "+" alone and a chord ending in "++" name the plus key itself.
        string keyName;
        string[] modifiers;
        if (chord == "+")
        {
            keyName = "+";
            modifiers = [];
        }
        else if (chord.EndsWith("++", StringComparison.Ordinal))
        {
            keyName = "+";
            modifiers = chord[..^2].Split('+', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            var parts = chord.Split('+');
            keyName = parts[^1];
            modifiers = parts[..^1];
        }

        bool shift = false, alt = false, control = false;
        foreach (var modifier in modifiers)
        {
            switch (modifier.Trim().ToLowerInvariant())
            {
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "ctrl":
                case "control":
                    control = true;
                    break;
                default:
                    throw new FormatException($"Unknown modifier '{modifier}' in key chord '{chord}'.");
            }
        }

        if (keyName.Length == 1)
        {
            // "Ctrl+C" is written in capitals by convention and means the unshifted key; only a bare
            // capital ("T") stands for typing it, Shift and all.
            var c = modifiers.Length > 0 && char.IsAsciiLetter(keyName[0]) ? char.ToLowerInvariant(keyName[0]) : keyName[0];
            return FromChar(c, shift, alt, control);
        }

        if (NamedKeys.TryGetValue(keyName, out var named))
        {
            return new KeyEvent(named.Char, named.Key, shift, alt, control);
        }

        if (keyName.Length >= 2 && (keyName[0] == 'F' || keyName[0] == 'f')
            && int.TryParse(keyName[1..], out var functionNumber) && functionNumber is >= 1 and <= 24)
        {
            return new KeyEvent('\0', ConsoleKey.F1 + (functionNumber - 1), shift, alt, control);
        }

        throw new FormatException($"Unknown key '{keyName}' in key chord '{chord}'.");
    }

    /// <summary>The key event typing <paramref name="c"/> produces, with any extra modifiers held.</summary>
    public static KeyEvent FromChar(char c, bool shift = false, bool alt = false, bool control = false)
    {
        if (char.IsAsciiLetter(c))
        {
            var upper = char.ToUpperInvariant(c);
            var consoleKey = ConsoleKey.A + (upper - 'A');
            // A capital letter is a shifted key; Shift+a means the same.
            shift |= char.IsUpper(c);
            char keyChar = control
                ? (char)(upper - 'A' + 1)
                : shift ? upper : char.ToLowerInvariant(c);
            return new KeyEvent(keyChar, consoleKey, shift, alt, control);
        }

        if (char.IsAsciiDigit(c))
        {
            return new KeyEvent(c, ConsoleKey.D0 + (c - '0'), shift, alt, control);
        }

        var key = c switch
        {
            ' ' => ConsoleKey.Spacebar,
            '\r' or '\n' => ConsoleKey.Enter,
            '\t' => ConsoleKey.Tab,
            '\b' => ConsoleKey.Backspace,
            '\u001b' => ConsoleKey.Escape,
            '+' => ConsoleKey.Add,
            '-' => ConsoleKey.Subtract,
            '*' => ConsoleKey.Multiply,
            '/' => ConsoleKey.Divide,
            '.' => ConsoleKey.OemPeriod,
            ',' => ConsoleKey.OemComma,
            _ => ConsoleKey.Oem1,
        };
        return new KeyEvent(c == '\n' ? '\r' : c, key, shift, alt, control);
    }

    /// <summary>The key events typing <paramref name="text"/> produces, one per character.</summary>
    public static IReadOnlyList<KeyEvent> FromText(string text) => text.Select(c => FromChar(c)).ToArray();
}
