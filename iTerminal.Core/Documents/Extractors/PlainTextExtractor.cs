using System.Text;
using iTerminal.Core.Data.Documents;

namespace iTerminal.Core.Documents.Extractors;

/// <summary>
/// Files whose bytes already are their text: notes, logs, data files, source code.
///
/// <para>The only real work is deciding the encoding. A BOM is believed when there is one;
/// otherwise the bytes are checked for valid UTF-8 and read as UTF-8 if they are, falling back to
/// Latin-1 — which cannot fail and cannot lose a byte — rather than to UTF-8 with replacement
/// characters. That distinction matters to the index and not just to the eye: a mis-decoded byte
/// becomes U+FFFD, and a word containing one is a token no query will ever match.</para>
/// </summary>
public sealed class PlainTextExtractor : IDocumentTextExtractor
{
    /// <summary>
    /// How much of a file is sniffed to decide whether it is UTF-8. A prefix is enough — a file that
    /// is UTF-8 at all is UTF-8 from the start — and reading all of a large log to answer a question
    /// about its encoding would double the cost of indexing it.
    /// </summary>
    private const int EncodingSniffLength = 64 * 1024;

    public IReadOnlyCollection<string> Extensions { get; } =
    [
        ".txt", ".text", ".md", ".markdown", ".rst", ".log", ".csv", ".tsv", ".json", ".yaml", ".yml",
        ".ini", ".cfg", ".conf", ".toml", ".sql", ".tex", ".srt", ".vtt",
        ".cs", ".fs", ".vb", ".c", ".h", ".cpp", ".hpp", ".java", ".kt", ".go", ".rs", ".rb", ".php",
        ".py", ".js", ".ts", ".jsx", ".tsx", ".sh", ".bash", ".ps1", ".psm1", ".bat", ".cmd",
    ];

    public DocumentTextResult Extract(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
            {
                return DocumentTextResult.None(DocumentExtractionStatus.Empty);
            }

            string text = Decode(bytes);
            string normalized = DocumentTextLimits.Normalize(text);

            return normalized.Length == 0
                ? DocumentTextResult.None(DocumentExtractionStatus.Empty)
                : new DocumentTextResult(normalized, DocumentExtractionStatus.Extracted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return DocumentTextResult.None(DocumentExtractionStatus.Failed, exception.Message);
        }
    }

    /// <summary>
    /// Turns bytes into a string using the most likely encoding. Shared with the extractors that end
    /// up holding a byte array of unknown encoding of their own (HTML meta charsets, PNG text
    /// chunks), so every path through the indexer decodes the same way.
    /// </summary>
    internal static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (LooksLikeUtf8(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Whether a prefix of <paramref name="bytes"/> decodes as UTF-8 without a single invalid
    /// sequence. A throwing decoder is used rather than eyeballing lead bytes because "valid UTF-8"
    /// is exactly the question, and the framework already answers it correctly.
    ///
    /// <para>A multi-byte character straddling the end of the sniffed prefix would throw and libel
    /// the whole file as non-UTF-8, so the prefix is trimmed back off any trailing continuation
    /// bytes before it is tested.</para>
    /// </summary>
    private static bool LooksLikeUtf8(byte[] bytes)
    {
        int length = Math.Min(bytes.Length, EncodingSniffLength);

        if (length < bytes.Length)
        {
            while (length > 0 && (bytes[length - 1] & 0xC0) == 0x80)
            {
                length--;
            }

            // Whatever precedes the continuation bytes is the lead byte of that same character.
            if (length > 0 && (bytes[length - 1] & 0x80) != 0)
            {
                length--;
            }
        }

        try
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes, 0, length);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
