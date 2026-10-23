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

    /// <summary>Line breaks and tabs become single spaces, the ends are trimmed: for a field that holds one line.</summary>
    public static string SingleLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
}
