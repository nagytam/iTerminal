using System.Text;
using System.Text.RegularExpressions;

namespace iConsole.Services;

/// <summary>
/// Reads an e-mail message (.eml, MIME) into lines: the addresses, date and subject, then the body as text. A
/// multipart message gives its plain-text part, or its HTML part as text when there is no plain text. Attachments are
/// listed by name at the end, not shown. Headers are unfolded and their encoded words (RFC 2047) decoded; bodies are
/// decoded from base64 or quoted-printable and read in the charset the part declares.
/// </summary>
public static class MimeMessageReader
{
    private static readonly Regex EncodedWord = new(@"=\?([^?\s]+)\?([BbQq])\?([^?\s]*)\?=", RegexOptions.Compiled);

    static MimeMessageReader()
    {
        // Windows code pages (ISO-8859-2, windows-1250 …) are not in the base framework.
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<string> Read(byte[] content)
    {
        try
        {
            MimePart message = MimePart.Parse(content);
            MessageParts parts = new();
            Collect(message, parts);

            return MessageLines.Format(
                from: message.Header("from"),
                to: message.Header("to"),
                cc: message.Header("cc"),
                date: message.Header("date"),
                subject: message.Header("subject"),
                plainText: parts.PlainText,
                html: parts.Html,
                attachments: parts.Attachments);
        }
        catch (Exception exception)
        {
            return [$"[Could not read the message: {exception.Message}]"];
        }
    }

    /// <summary>What a message body offers: the first plain and HTML text parts and the attachments' names.</summary>
    private sealed class MessageParts
    {
        public string? PlainText { get; set; }
        public string? Html { get; set; }
        public List<string> Attachments { get; } = [];
    }

    private static void Collect(MimePart part, MessageParts parts)
    {
        if (part.MediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            foreach (MimePart child in part.Children())
            {
                Collect(child, parts);
            }

            return;
        }

        string? disposition = part.Header("content-disposition");
        bool isAttachment = disposition is not null && disposition.StartsWith("attachment", StringComparison.OrdinalIgnoreCase);
        bool isText = part.MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
        if (isAttachment || !isText)
        {
            // A part that is not text is an attachment, and so is a message inside the message.
            string fallback = part.MediaType.Equals("message/rfc822", StringComparison.OrdinalIgnoreCase) ? "(message)" : "(unnamed)";
            parts.Attachments.Add(part.FileName() ?? fallback);
            return;
        }

        if (part.MediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            parts.PlainText ??= part.TextBody();
        }
        else if (part.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
        {
            parts.Html ??= part.TextBody();
        }
    }

    /// <summary>Decodes an RFC 2047 header value: encoded words become text, and the space between two of them is dropped.</summary>
    internal static string DecodeHeader(string value)
    {
        string joined = Regex.Replace(value, @"\?=\s+=\?", "?==?");
        return EncodedWord.Replace(joined, match =>
        {
            byte[] bytes = match.Groups[2].Value.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? DecodeBase64(match.Groups[3].Value)
                : QuotedPrintable(match.Groups[3].Value.Replace('_', ' '));
            return CharsetDecode(bytes, match.Groups[1].Value);
        });
    }

    private static byte[] DecodeBase64(string text)
    {
        try
        {
            return Convert.FromBase64String(new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()));
        }
        catch (FormatException)
        {
            return Encoding.Latin1.GetBytes(text);
        }
    }

    /// <summary>Quoted-printable: =XX is a byte, and an = at the end of a line joins it to the next.</summary>
    internal static byte[] QuotedPrintable(string text)
    {
        List<byte> bytes = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '=' && i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
            {
                bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (c == '=' && i + 1 < text.Length && text[i + 1] is '\r' or '\n')
            {
                // A soft line break: the line goes on.
                i += text[i + 1] == '\r' && i + 2 < text.Length && text[i + 2] == '\n' ? 2 : 1;
            }
            else
            {
                bytes.Add((byte)c);
            }
        }

        return [.. bytes];
    }

    /// <summary>Decodes bytes in the named charset; an unknown charset is read as UTF-8.</summary>
    internal static string CharsetDecode(byte[] bytes, string charset)
    {
        try
        {
            return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>One MIME entity: its headers and its body, both as raw bytes read one byte per character.</summary>
    private sealed class MimePart
    {
        private readonly Dictionary<string, string> _headers;
        private readonly string _body;

        private MimePart(Dictionary<string, string> headers, string body)
        {
            _headers = headers;
            _body = body;
        }

        public string MediaType => (Header("content-type") ?? "text/plain").Split(';')[0].Trim();

        public static MimePart Parse(byte[] content)
        {
            string text = Encoding.Latin1.GetString(content);
            int split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            int skip = 4;
            if (split < 0)
            {
                split = text.IndexOf("\n\n", StringComparison.Ordinal);
                skip = 2;
            }

            string headerText = split < 0 ? text : text[..split];
            string body = split < 0 ? string.Empty : text[(split + skip)..];

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            string? name = null;
            foreach (string line in headerText.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                if (line.Length > 0 && char.IsWhiteSpace(line[0]) && name is not null)
                {
                    // A folded header continues on a line that starts with whitespace.
                    headers[name] += " " + HeaderText(line.Trim());
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                name = line[..colon].Trim();
                headers.TryAdd(name, HeaderText(line[(colon + 1)..].Trim()));
            }

            return new MimePart(headers, body);
        }

        /// <summary>A header read as UTF-8: an unencoded non-ASCII header is UTF-8 in practice.</summary>
        private static string HeaderText(string raw) => Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(raw));

        public string? Header(string name) => _headers.TryGetValue(name, out string? value) ? value : null;

        /// <summary>The body decoded from its transfer encoding and read in its charset.</summary>
        public string TextBody()
        {
            string encoding = (Header("content-transfer-encoding") ?? "7bit").ToLowerInvariant();
            byte[] bytes = encoding switch
            {
                "base64" => DecodeBase64(_body),
                "quoted-printable" => QuotedPrintable(_body),
                _ => Encoding.Latin1.GetBytes(_body),
            };

            return CharsetDecode(bytes, Parameter("content-type", "charset") ?? "utf-8");
        }

        public string? FileName()
        {
            string? name = Parameter("content-disposition", "filename") ?? Parameter("content-type", "name");
            return name is null ? null : DecodeHeader(name);
        }

        /// <summary>The sub-parts of a multipart body, split on its boundary.</summary>
        public IEnumerable<MimePart> Children()
        {
            string? boundary = Parameter("content-type", "boundary");
            if (boundary is null)
            {
                yield break;
            }

            string delimiter = "--" + boundary;
            string[] sections = Regex.Split(_body, @"(?:\r?\n|^)" + Regex.Escape(delimiter));
            foreach (string section in sections.Skip(1))
            {
                if (section.StartsWith("--", StringComparison.Ordinal))
                {
                    // The closing delimiter: nothing follows it.
                    yield break;
                }

                string trimmed = section.TrimStart('\r', '\n');
                if (trimmed.Length > 0)
                {
                    yield return Parse(Encoding.Latin1.GetBytes(trimmed));
                }
            }
        }

        /// <summary>A parameter of a header, such as charset in Content-Type.</summary>
        private string? Parameter(string header, string parameter)
        {
            string? value = Header(header);
            if (value is null)
            {
                return null;
            }

            foreach (string part in value.Split(';').Skip(1))
            {
                int equals = part.IndexOf('=');
                if (equals > 0 && part[..equals].Trim().Equals(parameter, StringComparison.OrdinalIgnoreCase))
                {
                    return part[(equals + 1)..].Trim().Trim('"');
                }
            }

            return null;
        }
    }
}

/// <summary>The shared layout of a message: its addresses, date and subject, then the body and the attachments.</summary>
internal static class MessageLines
{
    public static IReadOnlyList<string> Format(
        string? from,
        string? to,
        string? cc,
        string? date,
        string? subject,
        string? plainText,
        string? html,
        IReadOnlyList<string> attachments)
    {
        List<string> lines = [];
        AddField(lines, "From", from);
        AddField(lines, "To", to);
        AddField(lines, "Cc", cc);
        AddField(lines, "Date", date);
        AddField(lines, "Subject", subject);
        if (lines.Count > 0)
        {
            lines.Add(string.Empty);
        }

        if (plainText is not null)
        {
            lines.AddRange(plainText.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()));
        }
        else if (html is not null)
        {
            lines.AddRange(HtmlTextExtractor.Lines(html));
        }

        if (attachments.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(attachments.Select(name => $"[Attachment: {name}]"));
        }

        return OfficeTextRenderer.Trim(lines);
    }

    private static void AddField(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {MimeMessageReader.DecodeHeader(value).Trim()}");
        }
    }
}
