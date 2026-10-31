namespace iConsole.Localization;

/// <summary>
/// Picks the keyboard letter of a button, check box or label so that the letter is drawn in its caption. A hot key
/// fixed in English (the D of Delete) is invisible once the caption is translated (Löschen), so the key still worked
/// but the highlight never showed.
/// </summary>
public static class HotKeys
{
    /// <summary>
    /// <paramref name="preferred"/> when the caption contains it (ignoring case), so English and every translation that
    /// keeps the letter behave as before. Otherwise the first letter of the caption's last word, which is the word that
    /// names the action (folder, file, Ordner, Datei), so the highlight shows and the key works.
    /// </summary>
    public static char VisibleIn(string caption, char preferred)
    {
        if (caption.Contains(preferred, StringComparison.OrdinalIgnoreCase))
        {
            return preferred;
        }

        string[] words = caption.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int word = words.Length - 1; word >= 0; word--)
        {
            foreach (char letter in words[word])
            {
                if (char.IsLetter(letter))
                {
                    return letter;
                }
            }
        }

        return preferred;
    }
}
