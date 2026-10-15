namespace iConsole.Helper;

public static class ConsoleKeyHelper
{
    public static HashSet<char> TypeableCharacters { get; set; } =
    [
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
        'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-', '_', ' ',
        '.', '/', '\\', ':', '@'
    ];

    /// <summary>
    /// Whether <paramref name="c"/> is something a person can type into a text field: any printable
    /// character, in any script. <see cref="TypeableCharacters"/> used to be the whole answer, and it
    /// is ASCII letters, digits and six punctuation marks — so a folder called "Photos (2024)" or
    /// "Tom's notes", an accented name, or a URL with a <c>?</c> or <c>&amp;</c> in it could not be
    /// typed into any dialog at all.
    /// </summary>
    public static bool IsTypeable(char c) =>
        TypeableCharacters.Contains(c)
        || (!char.IsControl(c) && !char.IsSurrogate(c) && c != '﻿');

    /// <summary>
    /// Whether <paramref name="keyEvent"/> types its character rather than being a chord. Ctrl and Alt
    /// make a chord — except together, which is how Windows reports AltGr: on most non-US layouts
    /// that is the only way to type <c>@</c>, <c>{</c> or <c>\</c> (Hungarian: AltGr+V is <c>@</c>),
    /// and a field that refused it could not take an e-mail address or an <c>ssh://user@host</c>.
    /// A real Ctrl+Alt chord carries no printable character, so it is still not typed.
    /// </summary>
    public static bool IsTypingKey(Data.KeyEvent keyEvent) =>
        IsTypeable(keyEvent.KeyChar)
        && (keyEvent.Alt == keyEvent.Control);

    public static char ConsoleKeyToChar(this ConsoleKey consoleKey)
    {
        var ch = '\0';
        switch (consoleKey)
        {
            case ConsoleKey.D1: { ch = '1'; break; }
            case ConsoleKey.D2: { ch = '2'; break; }
            case ConsoleKey.D3: { ch = '3'; break; }
            case ConsoleKey.D4: { ch = '4'; break; }
            case ConsoleKey.D5: { ch = '5'; break; }
            case ConsoleKey.D6: { ch = '6'; break; }
            case ConsoleKey.D7: { ch = '7'; break; }
            case ConsoleKey.D8: { ch = '8'; break; }
            case ConsoleKey.D9: { ch = '9'; break; }
        }
        return ch;
    }
}
