using System.IO.Compression;
using System.Text;
using iTerminal.Core.Data.Documents;

namespace iTerminal.Core.Documents.Extractors;

/// <summary>
/// Text out of a PDF, with nothing but the framework behind it.
///
/// <para>Writing this rather than taking a PDF library is the same call the repository already made
/// for ID3 and FLAC tags, and it holds for the same reason: what is needed here is a fraction of
/// what a real PDF library does. There is no rendering, no page geometry, no forms and no
/// signatures — only "find the content streams, inflate them, and read the strings the text
/// operators draw". That fits in one file and costs the app no dependency, no native binary and no
/// licence.</para>
///
/// <para><b>How it finds the content.</b> A conforming reader would follow the trailer to the xref
/// table to the page tree. This one scans the file for <c>stream … endstream</c> pairs instead. That
/// is deliberate: a scan reads damaged and incrementally-updated files that a strict xref walk
/// rejects, it needs no cross-reference stream support, and for a text index the difference — text
/// from an object no page references any more — is a false positive nobody will notice, while a
/// document that failed to open is a real loss.</para>
///
/// <para><b>What it cannot do.</b> Fonts with a custom encoding and no simple mapping — subset CID
/// fonts above all — draw text whose byte codes are glyph indices, not characters. Reading those
/// properly means parsing <c>/ToUnicode</c> CMaps, which is a different order of work. Rather than
/// index the mojibake that would result, <see cref="LooksLikeText"/> checks what came out and the
/// document is recorded as <see cref="DocumentExtractionStatus.Empty"/> with a note saying why. The
/// same note covers scanned PDFs, which have no text objects at all — and for those the answer is
/// OCR, which <see cref="ImageTextExtractor"/> has and this one deliberately does not duplicate.</para>
/// </summary>
public sealed class PdfTextExtractor : IDocumentTextExtractor
{
    /// <summary>
    /// Cap on the inflated size of a single stream. A compression bomb is a real shape for a
    /// malformed PDF, and one stream is never legitimately this large in a text-bearing document.
    /// </summary>
    private const int MaxInflatedStreamBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Below this fraction of plausibly-textual characters, the extraction is treated as having
    /// produced nothing. See the class remarks: garbage in the index is worse than a gap in it,
    /// because a gap is visible and garbage is not.
    /// </summary>
    private const double MinimumTextRatio = 0.6;

    /// <summary>Short strings are too small a sample for <see cref="MinimumTextRatio"/> to judge.</summary>
    private const int TextRatioSampleThreshold = 40;

    public IReadOnlyCollection<string> Extensions { get; } = [".pdf"];

    public DocumentTextResult Extract(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return ExtractFromBytes(File.ReadAllBytes(path), cancellationToken);
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
    /// The whole extraction, from the file's bytes. Separated from <see cref="Extract"/> so tests
    /// can build a PDF in memory and assert on it without going through the disk.
    /// </summary>
    internal static DocumentTextResult ExtractFromBytes(byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (bytes.Length < 8 || !StartsWithHeader(bytes))
        {
            return DocumentTextResult.None(DocumentExtractionStatus.Failed, "not a PDF");
        }

        // Latin-1 maps every byte to the code point of the same value, so the structure — which is
        // ASCII — reads correctly and the binary stream payloads survive the round trip byte for
        // byte. Any other encoding would corrupt the payloads before they could be inflated.
        string raw = Encoding.Latin1.GetString(bytes);

        if (IsEncrypted(raw))
        {
            return DocumentTextResult.None(
                DocumentExtractionStatus.Failed,
                "the document is encrypted");
        }

        StringBuilder text = new();
        StringBuilder auxiliary = new();
        int index = 0;

        while (index < raw.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int streamKeyword = IndexOfStreamKeyword(raw, index);
            if (streamKeyword < 0)
            {
                break;
            }

            PdfStream? stream = ReadStream(raw, bytes, streamKeyword);
            if (stream is null)
            {
                index = streamKeyword + 6;
                continue;
            }

            index = stream.EndOffset;

            byte[]? content = Decode(stream);
            if (content is null)
            {
                continue;
            }

            string decoded = Encoding.Latin1.GetString(content);

            if (stream.IsObjectStream)
            {
                // Not page content: a container of the very objects — the document information
                // dictionary above all — that a compressed-xref PDF keeps out of the plain file. It
                // is kept aside so its dictionary text can be mined for the title without ever being
                // mistaken for something the page draws.
                auxiliary.Append(decoded).Append('\n');
                continue;
            }

            AppendContentStreamText(decoded, text);
        }

        string extracted = DocumentTextLimits.Normalize(text.ToString());
        string metadataSource = raw + auxiliary;

        string title = FindInfoValue(metadataSource, "Title");
        string author = FindInfoValue(metadataSource, "Author");
        int pageCount = CountPages(metadataSource);

        if (extracted.Length == 0)
        {
            return DocumentTextResult.None(
                DocumentExtractionStatus.Empty,
                "no text objects — the pages are probably scanned images") with
            {
                Title = title,
                Author = author,
                PageCount = pageCount,
            };
        }

        if (!LooksLikeText(extracted))
        {
            return DocumentTextResult.None(
                DocumentExtractionStatus.Empty,
                "text uses a font encoding that cannot be mapped back to characters") with
            {
                Title = title,
                Author = author,
                PageCount = pageCount,
            };
        }

        return new DocumentTextResult(extracted, DocumentExtractionStatus.Extracted)
        {
            Title = title,
            Author = author,
            PageCount = pageCount,
        };
    }

    private static bool StartsWithHeader(byte[] bytes)
    {
        // Some producers put junk before the header; the specification allows a reader to look for
        // it within the first kilobyte, and files in the wild rely on that.
        int limit = Math.Min(bytes.Length - 5, 1024);
        for (int index = 0; index <= limit; index++)
        {
            if (bytes[index] == '%' && bytes[index + 1] == 'P' && bytes[index + 2] == 'D'
                && bytes[index + 3] == 'F' && bytes[index + 4] == '-')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the document is encrypted. Only the trailer's <c>/Encrypt</c> reference counts —
    /// the word appears in plenty of unencrypted files as part of other names — so the search is
    /// anchored to the key form it takes in a dictionary.
    /// </summary>
    private static bool IsEncrypted(string raw)
    {
        int index = raw.IndexOf("/Encrypt", StringComparison.Ordinal);
        while (index >= 0)
        {
            int after = index + "/Encrypt".Length;
            if (after >= raw.Length || raw[after] == ' ' || raw[after] == '\r' || raw[after] == '\n' || raw[after] == '<')
            {
                return true;
            }

            index = raw.IndexOf("/Encrypt", after, StringComparison.Ordinal);
        }

        return false;
    }

    #region Stream discovery

    /// <summary>One <c>stream … endstream</c> pair together with the dictionary that introduced it.</summary>
    private sealed record PdfStream(string Dictionary, byte[] Data, int EndOffset)
    {
        public bool IsObjectStream => Dictionary.Contains("/ObjStm", StringComparison.Ordinal);
    }

    /// <summary>
    /// The next <c>stream</c> keyword at or after <paramref name="start"/>. The keyword has to stand
    /// alone: <c>endstream</c> ends with the same six letters, and matching it would restart the
    /// scan inside the stream that just ended.
    /// </summary>
    private static int IndexOfStreamKeyword(string raw, int start)
    {
        int index = raw.IndexOf("stream", start, StringComparison.Ordinal);
        while (index >= 0)
        {
            bool standalone = index == 0 || !char.IsLetter(raw[index - 1]);
            if (standalone)
            {
                return index;
            }

            index = raw.IndexOf("stream", index + 6, StringComparison.Ordinal);
        }

        return -1;
    }

    /// <summary>
    /// Reads the stream introduced at <paramref name="keywordOffset"/> along with its dictionary.
    ///
    /// <para>The declared <c>/Length</c> is used only as a hint and always verified against an
    /// <c>endstream</c> at the position it points to. Producers get it wrong — an incremental update
    /// that rewrote the stream and not its length is the classic case — and trusting a wrong length
    /// truncates the content silently. Searching for <c>endstream</c> is the fallback that cannot
    /// be wrong about where the data ends.</para>
    /// </summary>
    private static PdfStream? ReadStream(string raw, byte[] bytes, int keywordOffset)
    {
        string dictionary = ReadPrecedingDictionary(raw, keywordOffset);

        int dataStart = keywordOffset + "stream".Length;
        if (dataStart < raw.Length && raw[dataStart] == '\r')
        {
            dataStart++;
        }

        if (dataStart < raw.Length && raw[dataStart] == '\n')
        {
            dataStart++;
        }

        int dataEnd = -1;

        if (TryReadDictionaryInteger(dictionary, "/Length", out int declaredLength)
            && declaredLength >= 0
            && dataStart + declaredLength <= raw.Length
            && IsEndstreamNear(raw, dataStart + declaredLength))
        {
            dataEnd = dataStart + declaredLength;
        }

        if (dataEnd < 0)
        {
            int marker = raw.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (marker < 0)
            {
                return null;
            }

            dataEnd = marker;

            // The end-of-line before "endstream" belongs to the delimiter, not to the data.
            if (dataEnd > dataStart && raw[dataEnd - 1] == '\n')
            {
                dataEnd--;
            }

            if (dataEnd > dataStart && raw[dataEnd - 1] == '\r')
            {
                dataEnd--;
            }
        }

        if (dataEnd <= dataStart)
        {
            return new PdfStream(dictionary, [], dataEnd + "endstream".Length);
        }

        byte[] data = new byte[dataEnd - dataStart];
        Array.Copy(bytes, dataStart, data, 0, data.Length);

        int endOffset = raw.IndexOf("endstream", dataEnd, StringComparison.Ordinal);
        return new PdfStream(dictionary, data, endOffset < 0 ? dataEnd : endOffset + "endstream".Length);
    }

    /// <summary>Whether an <c>endstream</c> follows at <paramref name="offset"/>, give or take the line break.</summary>
    private static bool IsEndstreamNear(string raw, int offset)
    {
        for (int index = offset; index < Math.Min(offset + 4, raw.Length); index++)
        {
            if (raw.AsSpan(index).StartsWith("endstream", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The dictionary immediately before a <c>stream</c> keyword, as text. Found by walking back to
    /// the matching <c>&lt;&lt;</c> so that a nested dictionary — <c>/DecodeParms</c> is one, and it
    /// is the one that matters — cannot be mistaken for the start of the outer one.
    /// </summary>
    private static string ReadPrecedingDictionary(string raw, int keywordOffset)
    {
        // LastIndexOf returns the index of the *first* '>' of the pair, so the walk starts one to
        // the right of it: the loop below reads a delimiter from its second character backwards.
        int close = raw.LastIndexOf(">>", keywordOffset, StringComparison.Ordinal);
        if (close < 0 || close + 1 >= raw.Length)
        {
            return string.Empty;
        }

        int depth = 0;
        for (int index = close + 1; index >= 1; index--)
        {
            if (raw[index] == '>' && raw[index - 1] == '>')
            {
                depth++;
                index--;
                continue;
            }

            if (raw[index] == '<' && raw[index - 1] == '<')
            {
                depth--;
                index--;
                if (depth == 0)
                {
                    return raw[index..(close + 2)];
                }
            }
        }

        return string.Empty;
    }

    private static bool TryReadDictionaryInteger(string dictionary, string key, out int value)
    {
        value = 0;
        int index = dictionary.IndexOf(key, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        index += key.Length;
        while (index < dictionary.Length && char.IsWhiteSpace(dictionary[index]))
        {
            index++;
        }

        int start = index;
        while (index < dictionary.Length && char.IsAsciiDigit(dictionary[index]))
        {
            index++;
        }

        // An indirect length ("/Length 42 0 R") is a reference, not a number; the endstream search
        // is what resolves those.
        return index > start && int.TryParse(dictionary.AsSpan(start, index - start), out value);
    }

    #endregion

    #region Decoding

    /// <summary>
    /// Inflates a stream's data, or returns null when there is nothing worth reading in it.
    ///
    /// <para>Images are skipped by their dictionary rather than by trying and failing to inflate
    /// them: a JPEG's bytes are perfectly valid input to a scan that does not know better, and the
    /// operator-shaped noise inside one would otherwise end up in the index.</para>
    /// </summary>
    private static byte[]? Decode(PdfStream stream)
    {
        if (stream.Data.Length == 0)
        {
            return null;
        }

        string dictionary = stream.Dictionary;

        if (dictionary.Contains("/Image", StringComparison.Ordinal)
            || dictionary.Contains("/DCTDecode", StringComparison.Ordinal)
            || dictionary.Contains("/JPXDecode", StringComparison.Ordinal)
            || dictionary.Contains("/CCITTFaxDecode", StringComparison.Ordinal)
            || dictionary.Contains("/JBIG2Decode", StringComparison.Ordinal))
        {
            return null;
        }

        byte[] data = stream.Data;

        if (dictionary.Contains("/FlateDecode", StringComparison.Ordinal))
        {
            byte[]? inflated = Inflate(data);
            if (inflated is null)
            {
                return null;
            }

            data = inflated;
        }
        else if (dictionary.Contains("/Filter", StringComparison.Ordinal))
        {
            // LZW, RunLength and the ASCII filters are all legal and none is worth carrying an
            // implementation for: between them they account for a vanishing share of files written
            // this century, and a stream skipped is a gap, not a corruption.
            return null;
        }

        if (TryReadDictionaryInteger(dictionary, "/Predictor", out int predictor) && predictor >= 10)
        {
            int columns = TryReadDictionaryInteger(dictionary, "/Columns", out int declared) ? declared : 1;
            int colors = TryReadDictionaryInteger(dictionary, "/Colors", out int declaredColors) ? declaredColors : 1;
            int bits = TryReadDictionaryInteger(dictionary, "/BitsPerComponent", out int declaredBits) ? declaredBits : 8;
            data = UndoPngPredictor(data, columns, colors, bits);
        }

        return data;
    }

    private static byte[]? Inflate(byte[] data)
    {
        // A zlib header is the conforming case; a few producers emit raw deflate. Trying zlib first
        // and falling back costs one failed read of a header and covers both.
        byte[]? result = TryInflate(data, zlib: true) ?? TryInflate(data, zlib: false);
        if (result is not null)
        {
            return result;
        }

        // Leading whitespace between "stream" and the data is legal and defeats both readers, since
        // it shifts the header they are looking for.
        int offset = 0;
        while (offset < data.Length && (data[offset] == '\r' || data[offset] == '\n' || data[offset] == ' '))
        {
            offset++;
        }

        if (offset == 0 || offset >= data.Length)
        {
            return null;
        }

        byte[] trimmed = data[offset..];
        return TryInflate(trimmed, zlib: true) ?? TryInflate(trimmed, zlib: false);
    }

    private static byte[]? TryInflate(byte[] data, bool zlib)
    {
        try
        {
            using MemoryStream input = new(data);
            using Stream decompressor = zlib
                ? new ZLibStream(input, CompressionMode.Decompress)
                : new DeflateStream(input, CompressionMode.Decompress);
            using MemoryStream output = new();

            byte[] buffer = new byte[81920];
            int read;
            while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxInflatedStreamBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, read);
            }

            // A stream that inflates to nothing is indistinguishable from one that failed, and
            // treating it as success would hide a genuine failure from the fallback path.
            return output.Length == 0 ? null : output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reverses the PNG row predictor that <c>/DecodeParms</c> can put in front of Flate. Object and
    /// cross-reference streams routinely use it, and without this their bytes come out shifted by a
    /// per-row filter byte — which is enough to hide the document title that is the only reason
    /// those streams are read here at all.
    /// </summary>
    internal static byte[] UndoPngPredictor(byte[] data, int columns, int colors, int bitsPerComponent)
    {
        int bytesPerPixel = Math.Max(1, colors * bitsPerComponent / 8);
        int rowLength = Math.Max(1, columns * colors * bitsPerComponent / 8);
        int stride = rowLength + 1;

        if (data.Length < stride)
        {
            return data;
        }

        int rows = data.Length / stride;
        byte[] output = new byte[rows * rowLength];
        byte[] previous = new byte[rowLength];

        for (int row = 0; row < rows; row++)
        {
            int sourceOffset = row * stride;
            byte filter = data[sourceOffset];
            byte[] current = new byte[rowLength];
            Array.Copy(data, sourceOffset + 1, current, 0, rowLength);

            for (int index = 0; index < rowLength; index++)
            {
                int left = index >= bytesPerPixel ? current[index - bytesPerPixel] : 0;
                int up = previous[index];
                int upperLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : 0;

                current[index] = filter switch
                {
                    1 => (byte)(current[index] + left),
                    2 => (byte)(current[index] + up),
                    3 => (byte)(current[index] + ((left + up) / 2)),
                    4 => (byte)(current[index] + Paeth(left, up, upperLeft)),
                    _ => current[index],
                };
            }

            Array.Copy(current, 0, output, row * rowLength, rowLength);
            previous = current;
        }

        return output;
    }

    private static byte Paeth(int left, int up, int upperLeft)
    {
        int estimate = left + up - upperLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpperLeft = Math.Abs(estimate - upperLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpperLeft)
        {
            return (byte)left;
        }

        return distanceUp <= distanceUpperLeft ? (byte)up : (byte)upperLeft;
    }

    #endregion

    #region Content stream reading

    /// <summary>
    /// Below this <c>TJ</c> adjustment — thousandths of a text-space unit, negative meaning "move
    /// right" — the gap the producer asked for is a word space rather than kerning. Producers vary,
    /// but nothing kerns by a fifth of an em, and nothing sets a word space much smaller.
    /// </summary>
    private const double WordSpaceAdjustment = -180;

    /// <summary>
    /// Walks a decoded content stream and appends the text its operators draw.
    ///
    /// <para>Only the operators that produce or position text are understood; everything else is a
    /// token to step over. The positioning ones matter as much as the drawing ones — a PDF has no
    /// concept of a line, so without <c>Td</c>, <c>TD</c> and <c>T*</c> becoming line breaks, a page
    /// arrives as one run-on line and every phrase that spans a line break stops matching.</para>
    /// </summary>
    private static void AppendContentStreamText(string content, StringBuilder output)
    {
        List<object> operands = [];
        int index = 0;

        while (index < content.Length)
        {
            char character = content[index];

            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            switch (character)
            {
                case '%':
                    while (index < content.Length && content[index] != '\n' && content[index] != '\r')
                    {
                        index++;
                    }

                    continue;

                case '(':
                    operands.Add(ReadLiteralString(content, ref index));
                    continue;

                case '<' when index + 1 < content.Length && content[index + 1] == '<':
                    SkipDictionary(content, ref index);
                    continue;

                case '<':
                    operands.Add(ReadHexString(content, ref index));
                    continue;

                case '[':
                case ']':
                case '{':
                case '}':
                    // Array delimiters carry no meaning the reader needs: TJ takes everything
                    // accumulated since the last operator, so the elements alone are enough.
                    index++;
                    continue;

                case '/':
                    index++;
                    while (index < content.Length && !IsDelimiter(content[index]) && !char.IsWhiteSpace(content[index]))
                    {
                        index++;
                    }

                    continue;
            }

            if (character is '+' or '-' or '.' || char.IsAsciiDigit(character))
            {
                int start = index;
                index++;
                while (index < content.Length && (content[index] == '.' || char.IsAsciiDigit(content[index])))
                {
                    index++;
                }

                if (double.TryParse(content.AsSpan(start, index - start),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double number))
                {
                    operands.Add(number);
                }

                continue;
            }

            int operatorStart = index;
            while (index < content.Length && !IsDelimiter(content[index]) && !char.IsWhiteSpace(content[index]))
            {
                index++;
            }

            if (index == operatorStart)
            {
                // An unexpected delimiter; stepping over it is the only way to make progress.
                index++;
                continue;
            }

            ApplyOperator(content[operatorStart..index], operands, output);
            operands.Clear();
        }
    }

    private static void ApplyOperator(string @operator, List<object> operands, StringBuilder output)
    {
        switch (@operator)
        {
            case "Tj":
            case "'":
            case "\"":
                if (@operator != "Tj")
                {
                    // Both move to the next line before drawing.
                    output.Append('\n');
                }

                AppendStrings(operands, output);
                break;

            case "TJ":
                AppendAdjustedStrings(operands, output);
                break;

            case "Td":
            case "TD":
            case "T*":
            case "ET":
                output.Append('\n');
                break;
        }
    }

    private static void AppendStrings(List<object> operands, StringBuilder output)
    {
        foreach (object operand in operands)
        {
            if (operand is string value)
            {
                output.Append(value);
            }
        }
    }

    /// <summary>
    /// The <c>TJ</c> case: an array of alternating strings and position adjustments, where a large
    /// enough adjustment is how most producers write a space.
    /// </summary>
    private static void AppendAdjustedStrings(List<object> operands, StringBuilder output)
    {
        foreach (object operand in operands)
        {
            switch (operand)
            {
                case string value:
                    output.Append(value);
                    break;

                case double adjustment when adjustment <= WordSpaceAdjustment:
                    if (output.Length > 0 && output[^1] != ' ' && output[^1] != '\n')
                    {
                        output.Append(' ');
                    }

                    break;
            }
        }
    }

    private static bool IsDelimiter(char character) =>
        character is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

    private static void SkipDictionary(string content, ref int index)
    {
        int depth = 0;

        while (index < content.Length)
        {
            if (index + 1 < content.Length && content[index] == '<' && content[index + 1] == '<')
            {
                depth++;
                index += 2;
                continue;
            }

            if (index + 1 < content.Length && content[index] == '>' && content[index + 1] == '>')
            {
                depth--;
                index += 2;
                if (depth <= 0)
                {
                    return;
                }

                continue;
            }

            index++;
        }
    }

    /// <summary>
    /// Reads a <c>(…)</c> literal string, honouring escapes and the fact that balanced parentheses
    /// inside one need no escaping at all — which is why this cannot be a search for the next
    /// <c>)</c>.
    /// </summary>
    internal static string ReadLiteralString(string content, ref int index)
    {
        index++; // the opening '(', consumed here so callers do not each have to remember to
        StringBuilder builder = new();
        int depth = 0;

        while (index < content.Length)
        {
            char character = content[index++];

            if (character == '\\')
            {
                if (index >= content.Length)
                {
                    break;
                }

                char escape = content[index++];
                switch (escape)
                {
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case '(': builder.Append('('); break;
                    case ')': builder.Append(')'); break;
                    case '\\': builder.Append('\\'); break;

                    case '\r':
                        // A backslash before a line break is a continuation, and \r\n counts once.
                        if (index < content.Length && content[index] == '\n')
                        {
                            index++;
                        }

                        break;

                    case '\n':
                        break;

                    default:
                        if (char.IsAsciiDigit(escape))
                        {
                            int value = escape - '0';
                            for (int digit = 0; digit < 2 && index < content.Length && char.IsAsciiDigit(content[index]); digit++)
                            {
                                value = (value * 8) + (content[index++] - '0');
                            }

                            builder.Append((char)(value & 0xFF));
                        }
                        else
                        {
                            builder.Append(escape);
                        }

                        break;
                }

                continue;
            }

            if (character == '(')
            {
                depth++;
                builder.Append('(');
                continue;
            }

            if (character == ')')
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
                builder.Append(')');
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a <c>&lt;…&gt;</c> hex string. A leading byte-order mark means the string is UTF-16BE,
    /// which is how a document title with anything but ASCII in it is written.
    /// </summary>
    internal static string ReadHexString(string content, ref int index)
    {
        index++; // the '<'
        List<byte> bytes = [];
        int pending = -1;

        while (index < content.Length && content[index] != '>')
        {
            char character = content[index++];
            int digit = Convert.ToInt32(Uri.IsHexDigit(character) ? Uri.FromHex(character) : -1);
            if (digit < 0)
            {
                continue;
            }

            if (pending < 0)
            {
                pending = digit;
                continue;
            }

            bytes.Add((byte)((pending << 4) | digit));
            pending = -1;
        }

        if (pending >= 0)
        {
            // An odd number of digits: the specification says the missing one is zero.
            bytes.Add((byte)(pending << 4));
        }

        if (index < content.Length)
        {
            index++; // the '>'
        }

        byte[] array = [.. bytes];

        return array.Length >= 2 && array[0] == 0xFE && array[1] == 0xFF
            ? Encoding.BigEndianUnicode.GetString(array, 2, array.Length - 2)
            : Encoding.Latin1.GetString(array);
    }

    #endregion

    #region Metadata

    /// <summary>
    /// A value from the document information dictionary, searched for across the raw file and the
    /// inflated object streams together. Both are needed: a classic PDF keeps the dictionary in the
    /// plain file, one with a compressed cross-reference keeps it inside an object stream.
    /// </summary>
    internal static string FindInfoValue(string source, string key)
    {
        string needle = "/" + key;
        int index = source.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            int cursor = index + needle.Length;

            // "/Titlecase" is not "/Title". A name ends where a delimiter or whitespace begins.
            if (cursor < source.Length && !char.IsWhiteSpace(source[cursor]) && source[cursor] != '(' && source[cursor] != '<')
            {
                index = source.IndexOf(needle, cursor, StringComparison.Ordinal);
                continue;
            }

            while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            {
                cursor++;
            }

            if (cursor < source.Length && source[cursor] == '(')
            {
                string value = ReadLiteralString(source, ref cursor);
                string cleaned = CleanMetadata(value);
                if (cleaned.Length > 0)
                {
                    return cleaned;
                }
            }
            else if (cursor < source.Length && source[cursor] == '<' && cursor + 1 < source.Length && source[cursor + 1] != '<')
            {
                string value = ReadHexString(source, ref cursor);
                string cleaned = CleanMetadata(value);
                if (cleaned.Length > 0)
                {
                    return cleaned;
                }
            }

            index = source.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return string.Empty;
    }

    private static string CleanMetadata(string value)
    {
        string cleaned = MarkupTextExtractor.CollapseWhitespace(value).Replace("\n", " ").Trim();
        return cleaned.Length > 512 ? cleaned[..512] : cleaned;
    }

    /// <summary>
    /// Pages, counted from the page objects themselves. <c>/Type /Pages</c> nodes are excluded —
    /// they are the interior nodes of the page tree, and counting them would inflate the total by
    /// the shape of a tree the reader never walks.
    /// </summary>
    internal static int CountPages(string source)
    {
        int count = 0;
        int index = source.IndexOf("/Type", StringComparison.Ordinal);

        while (index >= 0)
        {
            int cursor = index + "/Type".Length;
            while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            {
                cursor++;
            }

            if (cursor < source.Length && source[cursor] == '/')
            {
                int nameStart = ++cursor;
                while (cursor < source.Length && !IsDelimiter(source[cursor]) && !char.IsWhiteSpace(source[cursor]))
                {
                    cursor++;
                }

                if (source.AsSpan(nameStart, cursor - nameStart).Equals("Page", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            index = source.IndexOf("/Type", index + "/Type".Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>
    /// Whether a piece of extracted text is plausibly language rather than glyph codes. See the
    /// class remarks for why this check exists at all.
    /// </summary>
    internal static bool LooksLikeText(string text)
    {
        if (text.Length < TextRatioSampleThreshold)
        {
            return true;
        }

        int plausible = 0;
        foreach (char character in text)
        {
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) || char.IsPunctuation(character)
                || char.IsSymbol(character))
            {
                plausible++;
            }
        }

        return (double)plausible / text.Length >= MinimumTextRatio;
    }

    #endregion
}
