namespace iTerminal.Core.Controls.Terminals;

/// <summary>
/// Formats a one-row prompt (a fixed prefix followed by what the user is typing) for a row of a
/// given width. The prompts used to write the whole line and let the buffer clip it on the right —
/// which clips exactly the end being typed, so past the edge of the row every keystroke went
/// somewhere the user could not see. The prefix is kept and the <em>start</em> of the input is
/// elided instead.
/// </summary>
public static class PromptLine
{
    public const char Ellipsis = '…';

    public static string Format(string prefix, string input, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        // No terminal is wider than this; padding to int.MaxValue ran out of memory.
        width = Math.Min(width, 100_000);
        prefix ??= string.Empty;
        input ??= string.Empty;

        string line = prefix + input;
        if (line.Length <= width)
        {
            return line.PadRight(width);
        }

        if (prefix.Length + 2 > width)
        {
            // Not even room for the prefix and one character of input: the input still wins.
            return (Ellipsis + input[^Math.Min(input.Length, width - 1)..]).PadRight(width);
        }

        int tailLength = width - prefix.Length - 1;
        return prefix + Ellipsis + input[^tailLength..];
    }
}
