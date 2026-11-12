namespace iTerminal.Core.Services.Documents;

/// <summary>
/// Splits a document's extracted text into the passages the meaning index embeds.
///
/// <para>An embedding model reads a few hundred words well and a whole report badly, so a document is
/// embedded as overlapping windows and scored by its best window. The overlap keeps a sentence that
/// falls on a boundary whole in at least one window. Windows are cut at fixed offsets rather than at
/// word boundaries: a split word costs a little similarity, and finding word boundaries would need a
/// tokenizer per language.</para>
///
/// <para>The count is capped. A 300-page book would otherwise cost hundreds of requests and a vector
/// per window held in memory; the first <see cref="MaxChunks"/> windows cover the opening
/// <c>(MaxChunks - 1) * step + ChunkLength</c> characters, which is where a document says what it is about.</para>
/// </summary>
public static class DocumentChunker
{
    /// <summary>Characters per window.</summary>
    public const int ChunkLength = 1000;

    /// <summary>Characters shared by consecutive windows.</summary>
    public const int Overlap = 200;

    /// <summary>The most windows one document is embedded as.</summary>
    public const int MaxChunks = 24;

    private const int Step = ChunkLength - Overlap;

    /// <summary>The offset of every window, in order. Empty text has none.</summary>
    public static IReadOnlyList<int> ChunkStarts(string? text)
    {
        List<int> starts = [];
        if (string.IsNullOrEmpty(text))
        {
            return starts;
        }

        for (int start = 0; start < text.Length && starts.Count < MaxChunks; start += Step)
        {
            starts.Add(start);

            // The last window reaches the end of the text; a further one would only repeat its tail.
            if (start + ChunkLength >= text.Length)
            {
                break;
            }
        }

        return starts;
    }

    /// <summary>The window that begins at <paramref name="start"/>, clipped to the end of the text.</summary>
    public static string ChunkAt(string text, int start) =>
        TrimToCharacters(text.Substring(start, Math.Min(ChunkLength, text.Length - start)));

    /// <summary>
    /// Drops a surrogate half at either end, so a window cut through an emoji or another non-BMP character neither
    /// embeds nor displays a lone surrogate. The windows are fixed offsets, so this can shorten a window by one character.
    /// </summary>
    public static string TrimToCharacters(string window)
    {
        int start = window.Length > 0 && char.IsLowSurrogate(window[0]) ? 1 : 0;
        int end = window.Length > start && char.IsHighSurrogate(window[^1]) ? window.Length - 1 : window.Length;
        return window[start..end];
    }
}
