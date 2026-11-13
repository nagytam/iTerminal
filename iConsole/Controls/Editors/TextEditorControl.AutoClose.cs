namespace iConsole.Controls.Editors;

/// <summary>
/// Auto-close: typing an opening bracket or a quote adds its partner after the cursor, typing a closing bracket or quote
/// that is already next steps over it, and Backspace between a pair removes both halves.
/// </summary>
public partial class TextEditorControl
{
    /// <summary>
    /// Whether typing <c>(</c>, <c>[</c>, <c>{</c>, <c>"</c> or <c>'</c> adds the matching closing character, and Backspace
    /// between such a pair removes both. On by default, as in Notepad++.
    /// </summary>
    public bool AutoCloseBrackets { get; set; } = true;

    /// <summary>The character that closes <paramref name="c"/>, or <c>'\0'</c> when it does not open a pair.</summary>
    private static char ClosingFor(char c) => c switch
    {
        '(' => ')',
        '[' => ']',
        '{' => '}',
        '"' => '"',
        '\'' => '\'',
        _ => '\0',
    };

    private static bool IsClosing(char c) => c is ')' or ']' or '}';

    private static bool IsQuote(char c) => c is '"' or '\'';

    /// <summary>A pair is only added where the text after the cursor is the end of the line, a space or another closing character.</summary>
    private static bool CanPairBefore(char next) =>
        next == '\0' || char.IsWhiteSpace(next) || IsClosing(next) || IsQuote(next);

    /// <summary>
    /// Types <paramref name="c"/> with its partner when it opens a pair, or steps over the closing character that is already
    /// next. A quote after a letter or digit, or after another quote, is left as typed (an apostrophe in "don't"). Returns
    /// whether the key was taken.
    /// </summary>
    private bool TryAutoClose(char c)
    {
        if (c == '\0')
        {
            return false;
        }

        var lines = TextModel.Lines;
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, lines.Count - 1);
        var line = lines[row];
        var column = (int)Math.Min(ScrollableModel.CursorPosition.X, line.Length);
        var next = column < line.Length ? line[column] : '\0';
        var previous = column > 0 ? line[column - 1] : '\0';

        if ((IsClosing(c) || IsQuote(c)) && next == c)
        {
            ScrollableModel.CursorPosition.X = column + 1;
            StateHasChanged();
            return true;
        }

        var closing = ClosingFor(c);
        if (closing == '\0' || !CanPairBefore(next))
        {
            return false;
        }

        if (IsQuote(c) && (char.IsLetterOrDigit(previous) || IsQuote(previous)))
        {
            return false;
        }

        var before = Capture();
        lines[row] = line.Insert(column, c.ToString() + closing);
        ScrollableModel.CursorPosition.X = column + 1;
        FinishLineEdit(before, null);
        return true;
    }

    /// <summary>Backspace with the cursor between a pair (<c>(|)</c>, <c>"|"</c>) removes both halves. Returns whether it did.</summary>
    private bool TryAutoCloseBackspace()
    {
        var lines = TextModel.Lines;
        if (lines.Count == 0)
        {
            return false;
        }

        var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, lines.Count - 1);
        var line = lines[row];
        var column = (int)Math.Min(ScrollableModel.CursorPosition.X, line.Length);
        if (column == 0 || column >= line.Length || ClosingFor(line[column - 1]) != line[column])
        {
            return false;
        }

        var before = Capture();
        lines[row] = line.Remove(column - 1, 2);
        ScrollableModel.CursorPosition.X = column - 1;
        FinishLineEdit(before, null);
        return true;
    }
}
