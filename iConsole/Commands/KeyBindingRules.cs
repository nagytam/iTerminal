using iConsole.Data;

namespace iConsole.Commands;

/// <summary>
/// Which keys a user binding may not take, and why. The rules are the ones in CLAUDE.md and
/// <c>docs/architecture/key-patterns.md</c>: a key that types text, the Alt+printable type-to-search of the file
/// panel, AltGr (Ctrl+Alt together), the keys the focus and dialog code claims first, and the keys the terminal
/// host or the child process needs.
/// </summary>
public static class KeyBindingRules
{
    /// <summary>The reason <paramref name="key"/> may not be bound, or <c>null</c> if it may.</summary>
    public static string? Refusal(Key key)
    {
        if (key.Control && key.Alt)
        {
            return "Ctrl+Alt together is how AltGr types a character, so it is not a chord";
        }

        if (key.Alt && IsPrintable(key.ConsoleKey))
        {
            return "Alt with a letter, digit or symbol starts the type-to-search of the file panel";
        }

        if (!key.Control && !key.Alt && IsPrintable(key.ConsoleKey))
        {
            return "this key types a character into every text box";
        }

        if (key.ConsoleKey == ConsoleKey.Escape)
        {
            return "Esc closes the dialog or menu in front of you";
        }

        if (key.ConsoleKey == ConsoleKey.Tab)
        {
            return "Tab and Shift+Tab move the focus between panels and controls";
        }

        if (key.Control)
        {
            // These are the ASCII control codes another key already sends: Ctrl+M is Enter, Ctrl+I is Tab,
            // Ctrl+H is Backspace and Ctrl+J is a line feed. Ctrl+M is also reserved by many terminal hosts.
            switch (key.ConsoleKey)
            {
                case ConsoleKey.M:
                    return "Ctrl+M is the code for Enter on a terminal, and many terminal hosts reserve it";
                case ConsoleKey.I:
                    return "Ctrl+I is the code for Tab on a terminal";
                case ConsoleKey.H:
                    return "Ctrl+H is the code for Backspace on a terminal";
                case ConsoleKey.J:
                    return "Ctrl+J is a line feed on a terminal";
                case ConsoleKey.C:
                case ConsoleKey.Z:
                case ConsoleKey.Oem5:
                    return "Ctrl+C, Ctrl+Z and Ctrl+\\ belong to the shell or program that runs in the terminal";
                case ConsoleKey.O:
                    return "Ctrl+O hands the whole terminal to a shell";
                case ConsoleKey.Oem4:
                    return "Ctrl+[ is the code for Esc on a terminal, and closes the dialog in front";
            }
        }

        return null;
    }

    /// <summary>
    /// Letters, digits, space, the punctuation keys and the numeric keypad: the keys that type a character when no
    /// Ctrl or Alt is held. Shift does not change that (Shift+A types <c>A</c>).
    /// </summary>
    private static bool IsPrintable(ConsoleKey consoleKey) =>
        consoleKey is >= ConsoleKey.A and <= ConsoleKey.Z
            or >= ConsoleKey.D0 and <= ConsoleKey.D9
            or >= ConsoleKey.NumPad0 and <= ConsoleKey.NumPad9
            or ConsoleKey.Spacebar
            or ConsoleKey.Multiply
            or ConsoleKey.Add
            or ConsoleKey.Subtract
            or ConsoleKey.Decimal
            or ConsoleKey.Divide
            or ConsoleKey.OemPlus
            or ConsoleKey.OemComma
            or ConsoleKey.OemMinus
            or ConsoleKey.OemPeriod
            or >= ConsoleKey.Oem1 and <= ConsoleKey.Oem8
            or ConsoleKey.Oem102;
}
