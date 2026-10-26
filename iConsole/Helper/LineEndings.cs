namespace iConsole.Helper;

/// <summary>
/// Which line break a text file uses, so that saving it again writes the same one back. A file read on Windows
/// with <c>\n</c> breaks used to be written with <c>\r\n</c> (and the other way round on Linux) because the reader
/// split on <see cref="Environment.NewLine"/> and the writer joined with it: opening a file and saving it
/// changed every line.
/// </summary>
public static class LineEndings
{
    /// <summary>
    /// The break the text uses most: <c>\r\n</c>, <c>\n</c> or a lone <c>\r</c>. A text with no break at all, or with as
    /// many of two kinds, gets the system's, which is what a new file should have.
    /// </summary>
    public static string Detect(string text)
    {
        int crlf = 0;
        int lf = 0;
        int cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (c == '\n')
            {
                lf++;
            }
        }

        int most = Math.Max(crlf, Math.Max(lf, cr));
        if (most == 0)
        {
            return Environment.NewLine;
        }

        // A tie between two kinds goes to the system's, when it is one of them.
        string system = Environment.NewLine;
        int systemCount = system == "\r\n" ? crlf : system == "\n" ? lf : cr;
        if (systemCount == most)
        {
            return system;
        }

        return crlf == most ? "\r\n" : lf == most ? "\n" : "\r";
    }

    /// <summary>Splits on any of the three breaks, so a file with mixed endings still comes apart into its lines.</summary>
    public static string[] Split(string text) => text.ReplaceLineEndings("\n").Split('\n');
}
