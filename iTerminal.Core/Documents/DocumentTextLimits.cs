using System.Text;

namespace iTerminal.Core.Documents;

/// <summary>
/// The caps every extractor works under, and the tidying every extracted string goes through.
///
/// <para>Both exist for the same reason: the index has to survive whatever is in the folder. A
/// documents folder routinely contains a 400 MB database export with a <c>.txt</c> extension, and
/// an index that tried to hold its text would take the app down. Truncating is the right answer
/// rather than skipping — the first megabyte of a huge file is still worth searching.</para>
/// </summary>
public static class DocumentTextLimits
{
    /// <summary>
    /// Largest file an extractor will open. Beyond this the document is indexed by name only and
    /// recorded as <see cref="Data.Documents.DocumentExtractionStatus.TooLarge"/>, so the results
    /// list can say so instead of showing an empty document that looks like a failure.
    /// </summary>
    public const long MaxFileSize = 64L * 1024 * 1024;

    /// <summary>
    /// Largest amount of text kept per document. Beyond this the text is cut and
    /// <see cref="TruncationMarker"/> appended, which is also what tells the reader the preview is
    /// not the whole document.
    /// </summary>
    public const int MaxTextLength = 1_000_000;

    /// <summary>Appended to text that hit <see cref="MaxTextLength"/>.</summary>
    public const string TruncationMarker = "\n… [truncated]";

    /// <summary>
    /// Normalises extracted text: collapses line endings, drops control characters that a terminal
    /// cannot draw, squeezes runs of blank lines, and applies <see cref="MaxTextLength"/>.
    ///
    /// <para>Control characters are stripped rather than escaped because this text has two
    /// consumers and both are hurt by them — the preview pane would draw them as holes, and the FTS
    /// tokenizer would treat them as word characters and produce tokens nothing can match.</para>
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(Math.Min(text.Length, MaxTextLength) + TruncationMarker.Length);
        int consecutiveNewlines = 0;
        bool truncated = false;

        for (int index = 0; index < text.Length; index++)
        {
            if (builder.Length >= MaxTextLength)
            {
                truncated = true;
                break;
            }

            char character = text[index];

            // \r\n and a bare \r both mean "new line"; a \r kept as itself would send the terminal's
            // cursor back to column zero mid-preview.
            if (character == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    continue;
                }

                character = '\n';
            }

            if (character == '\n')
            {
                consecutiveNewlines++;
                if (consecutiveNewlines > 2)
                {
                    continue;
                }

                builder.Append('\n');
                continue;
            }

            consecutiveNewlines = 0;

            if (character == '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (char.IsControl(character) || character == '�')
            {
                continue;
            }

            builder.Append(character);
        }

        if (truncated)
        {
            builder.Append(TruncationMarker);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Words in a piece of extracted text. Counted by whitespace runs rather than by a regex: this
    /// runs once per indexed document and the number is only ever shown to a human, so the cheap
    /// definition is the right one.
    /// </summary>
    public static int CountWords(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int words = 0;
        bool inWord = false;

        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
                continue;
            }

            if (!inWord)
            {
                words++;
                inWord = true;
            }
        }

        return words;
    }
}
