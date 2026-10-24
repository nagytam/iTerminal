using iConsole.Data;
using iConsole.Services;

namespace iTerminal.Core.Helper;

/// <summary>
/// Paste for the dialogs that draw and edit their own text lines (the web browser's address bar, the mail
/// composer): Ctrl+V or Shift+Insert, from the same clipboard the text boxes use.
/// </summary>
internal static class PasteHelper
{
    public static bool IsPasteKey(KeyEvent key) =>
        (key.Control && !key.Alt && !key.Shift && key.ConsoleKey == ConsoleKey.V)
        || (key.Shift && !key.Control && !key.Alt && key.ConsoleKey == ConsoleKey.Insert);

    /// <summary>
    /// Reads the clipboard and hands the text to <paramref name="insert"/>. A clipboard that answers at once (the
    /// in-process one, most system ones) is used on the spot; one that makes the key handler wait is read on the
    /// side and applied when it lands, then <paramref name="changed"/> asks for a repaint.
    /// </summary>
    public static void Paste(Action<string> insert, Action changed)
    {
        Task<string?> read = TextClipboard.Current.GetTextAsync();
        if (read.IsCompletedSuccessfully)
        {
            Apply(read.Result);
            return;
        }

        _ = read.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                Apply(task.Result);
            }
        }, TaskScheduler.Default);

        void Apply(string? text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                insert(text);
                changed();
            }
        }
    }

    /// <summary>Ctrl+W or Ctrl+Backspace: delete the word before the cursor, as a shell does.</summary>
    public static bool IsDeleteWordKey(KeyEvent key) =>
        key.Control && !key.Alt && !key.Shift && key.ConsoleKey is ConsoleKey.W or ConsoleKey.Backspace;

    /// <summary>Ctrl+U: clear the line.</summary>
    public static bool IsClearKey(KeyEvent key) =>
        key.Control && !key.Alt && !key.Shift && key.ConsoleKey == ConsoleKey.U;

    /// <summary>
    /// <paramref name="text"/> without its last word: a final line break goes alone, otherwise the spaces at the end
    /// and the word before them, leaving the space that separated it from the one before.
    /// </summary>
    public static string DeleteLastWord(string text)
    {
        if (text.EndsWith('\n'))
        {
            return text[..^1];
        }

        string trimmed = text.TrimEnd(' ');
        int start = trimmed.LastIndexOfAny([' ', '\n']);
        return start < 0 ? string.Empty : trimmed[..(start + 1)];
    }

    /// <summary>Line breaks and tabs become single spaces, the ends are trimmed: for a field that holds one line.</summary>
    public static string SingleLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
}
