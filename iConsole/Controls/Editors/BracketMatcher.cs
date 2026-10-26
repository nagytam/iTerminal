namespace iConsole.Controls.Editors;

/// <summary>A place in the text: a line and a column, both counted from zero.</summary>
public readonly record struct BracketLocation(int Line, int Column);

/// <summary>A bracket and the one it pairs with; <see cref="Match"/> is null when it has none (an unclosed bracket).</summary>
public sealed record BracketMatch(BracketLocation Bracket, BracketLocation? Match);

/// <summary>
/// Finds the bracket that pairs with the one at, or just before, the cursor: <c>( )</c>, <c>[ ]</c> and <c>{ }</c>. Only
/// brackets of the same kind are counted, so <c>( [ ) ]</c> does not confuse a <c>(</c> looking for its <c>)</c>. Brackets
/// in strings and comments count like any other: the matcher reads characters, not syntax. The search gives up after
/// <see cref="MaxLinesScanned"/> lines or <see cref="MaxCharactersScanned"/> characters, so a huge file with an unclosed
/// bracket costs a bounded time on every frame.
/// </summary>
public static class BracketMatcher
{
    public const string Opening = "([{";
    public const string Closing = ")]}";
    public const int MaxLinesScanned = 5000;
    public const int MaxCharactersScanned = 500_000;

    /// <summary>
    /// The bracket under the cursor (the character at <paramref name="column"/>), else the one before it, and its partner;
    /// null when the cursor is not next to a bracket.
    /// </summary>
    public static BracketMatch? Find(IList<string> lines, int line, int column)
    {
        if (line < 0 || line >= lines.Count || column < 0)
        {
            return null;
        }

        string text = lines[line];
        int at = column;
        if (!IsBracket(text, at))
        {
            at = column - 1;
            if (!IsBracket(text, at))
            {
                return null;
            }
        }

        char bracket = text[at];
        BracketLocation here = new(line, at);
        int opening = Opening.IndexOf(bracket);
        BracketLocation? match = opening >= 0
            ? Scan(lines, here, bracket, Closing[opening], forward: true)
            : Scan(lines, here, bracket, Opening[Closing.IndexOf(bracket)], forward: false);
        return new BracketMatch(here, match);
    }

    private static bool IsBracket(string text, int index)
    {
        return index >= 0 && index < text.Length && (Opening.Contains(text[index]) || Closing.Contains(text[index]));
    }

    private static BracketLocation? Scan(IList<string> lines, BracketLocation start, char same, char other, bool forward)
    {
        int step = forward ? 1 : -1;
        int depth = 0;
        int scanned = 0;
        for (int line = start.Line; line >= 0 && line < lines.Count; line += step)
        {
            if (Math.Abs(line - start.Line) > MaxLinesScanned)
            {
                return null;
            }

            string text = lines[line];
            int from = line == start.Line ? start.Column + step : (forward ? 0 : text.Length - 1);
            for (int column = from; column >= 0 && column < text.Length; column += step)
            {
                if (++scanned > MaxCharactersScanned)
                {
                    return null;
                }

                char c = text[column];
                if (c == same)
                {
                    depth++;
                }
                else if (c == other)
                {
                    if (depth == 0)
                    {
                        return new BracketLocation(line, column);
                    }

                    depth--;
                }
            }
        }

        return null;
    }
}
