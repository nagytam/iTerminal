using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Model.Mail;

/// <summary>
/// Reads an RFC 5322 message as far as a mail reader needs it: the headers (unfolded, RFC 2047 words
/// decoded), and a text body picked from the MIME tree — the plain text part when there is one, else the
/// HTML part with its tags taken out. Base64 and quoted-printable are decoded and the declared charset
/// honoured. Attachments are skipped. It is written for the mail that is actually in an inbox, so a message
/// with a broken part still shows what it can rather than failing the whole read.
/// </summary>
public static partial class MimeParser
{
    public static MailItem Parse(int number, string raw)
    {
        var (headerBlock, body) = SplitHeaders(raw);
        var headers = ParseHeaders(headerBlock);
        return new MailItem(
            number,
            HeaderValue(headers, "From"),
            HeaderValue(headers, "To"),
            HeaderValue(headers, "Subject"),
            HeaderValue(headers, "Date"),
            BodyText(headers, body));
    }

    /// <summary>The list line for a message, from its headers alone (a TOP reply, or a whole RETR).</summary>
    public static MailListing ParseListing(int number, long size, string headerBlock)
    {
        var (block, _) = SplitHeaders(headerBlock);
        var headers = ParseHeaders(block);
        return new MailListing(number, size, HeaderValue(headers, "From"), HeaderValue(headers, "Subject"), HeaderValue(headers, "Date"));
    }

    /// <summary>The headers as text, and the body after the first blank line.</summary>
    internal static (string Headers, string Body) SplitHeaders(string raw)
    {
        var blank = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var length = 4;
        if (blank < 0)
        {
            blank = raw.IndexOf("\n\n", StringComparison.Ordinal);
            length = 2;
        }

        return blank < 0 ? (raw, string.Empty) : (raw[..blank], raw[(blank + length)..]);
    }

    /// <summary>Header names are case-insensitive; a repeated header keeps its first value.</summary>
    internal static Dictionary<string, string> ParseHeaders(string block)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        var value = new StringBuilder();

        void Store()
        {
            if (name is not null)
            {
                headers.TryAdd(name, value.ToString().Trim());
            }
        }

        foreach (var line in block.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.Length > 0 && (text[0] == ' ' || text[0] == '\t') && name is not null)
            {
                // A folded header: the continuation joins the value with a space.
                value.Append(' ').Append(text.Trim());
                continue;
            }

            Store();
            var colon = text.IndexOf(':');
            if (colon <= 0)
            {
                name = null;
                value.Clear();
                continue;
            }

            name = text[..colon].Trim();
            value.Clear().Append(text[(colon + 1)..].Trim());
        }

        Store();
        return headers;
    }

    private static string HeaderValue(Dictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) ? DecodeWords(value) : string.Empty;

    private static string BodyText(Dictionary<string, string> headers, string body)
    {
        string? plain = null;
        string? html = null;
        Collect(headers, body, ref plain, ref html);
        return plain ?? html ?? string.Empty;
    }

    private static void Collect(Dictionary<string, string> headers, string body, ref string? plain, ref string? html)
    {
        var contentType = headers.TryGetValue("Content-Type", out var raw) ? raw : "text/plain";
        var type = contentType.Split(';')[0].Trim().ToLowerInvariant();
        var disposition = headers.TryGetValue("Content-Disposition", out var d) ? d.ToLowerInvariant() : string.Empty;
        if (disposition.StartsWith("attachment", StringComparison.Ordinal))
        {
            return;
        }

        if (type.StartsWith("multipart/", StringComparison.Ordinal))
        {
            var boundary = Parameter(contentType, "boundary");
            if (boundary is null)
            {
                return;
            }

            foreach (var part in SplitParts(body, boundary))
            {
                var (partHeaders, partBody) = SplitHeaders(part);
                Collect(ParseHeaders(partHeaders), partBody, ref plain, ref html);
            }
        }
        else if (type == "text/plain" || type == string.Empty)
        {
            plain ??= Decode(headers, contentType, body);
        }
        else if (type == "text/html")
        {
            html ??= StripHtml(Decode(headers, contentType, body));
        }
    }

    private static IEnumerable<string> SplitParts(string body, string boundary)
    {
        var pieces = body.Split("--" + boundary);
        for (var i = 1; i < pieces.Length; i++)
        {
            if (pieces[i].StartsWith("--", StringComparison.Ordinal))
            {
                yield break;
            }

            // The line break before a boundary belongs to the boundary, so it is not part of the part.
            var part = pieces[i].TrimStart('\r', '\n');
            if (part.EndsWith("\r\n", StringComparison.Ordinal))
            {
                part = part[..^2];
            }
            else if (part.EndsWith('\n'))
            {
                part = part[..^1];
            }

            yield return part;
        }
    }

    internal static string? Parameter(string contentType, string name)
    {
        var match = Regex.Match(contentType, $@"{name}\s*=\s*(?:""([^""]*)""|([^;\s]+))", RegexOptions.IgnoreCase);
        return match.Success ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
    }

    private static string Decode(Dictionary<string, string> headers, string contentType, string body)
    {
        var encoding = headers.TryGetValue("Content-Transfer-Encoding", out var cte) ? cte.Trim().ToLowerInvariant() : string.Empty;
        var charset = Parameter(contentType, "charset") ?? "utf-8";
        try
        {
            switch (encoding)
            {
                case "base64":
                    var base64 = new string(body.Where(c => !char.IsWhiteSpace(c)).ToArray());
                    return CharsetOf(charset).GetString(Convert.FromBase64String(base64));
                case "quoted-printable":
                    return CharsetOf(charset).GetString(QuotedPrintable(body));
                default:
                    return body;
            }
        }
        catch (FormatException)
        {
            return body;
        }
    }

    private static Encoding CharsetOf(string name)
    {
        try
        {
            return Encoding.GetEncoding(name.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>Decodes quoted-printable into bytes: <c>=XX</c> is a byte, <c>=</c> at a line end is a soft break.</summary>
    internal static byte[] QuotedPrintable(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '=')
            {
                if (i + 1 < text.Length && (text[i + 1] == '\r' || text[i + 1] == '\n'))
                {
                    i += text[i + 1] == '\r' && i + 2 < text.Length && text[i + 2] == '\n' ? 2 : 1;
                    continue;
                }

                if (i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
                {
                    bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                    i += 2;
                    continue;
                }
            }

            bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
        }

        return [.. bytes];
    }

    internal static string StripHtml(string html)
    {
        var text = ScriptOrStyle().Replace(html, string.Empty);
        text = LineBreaks().Replace(text, "\n");
        text = Tags().Replace(text, string.Empty);
        return WebUtility.HtmlDecode(text).Replace('\u00A0', ' ').Trim();
    }

    /// <summary>
    /// Decodes RFC 2047 encoded words in a header (<c>=?utf-8?B?…?=</c>, <c>=?utf-8?Q?…?=</c>). Words that
    /// follow each other are joined without the space between them, as the standard says they are.
    /// </summary>
    internal static string DecodeWords(string value)
    {
        var joined = AdjacentWords().Replace(value, "?==?");
        return EncodedWord().Replace(joined, match =>
        {
            var charset = match.Groups[1].Value;
            var text = match.Groups[3].Value;
            try
            {
                var bytes = match.Groups[2].Value.Equals("B", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(text)
                    : QuotedWord(text);
                return CharsetOf(charset).GetString(bytes);
            }
            catch (FormatException)
            {
                return match.Value;
            }
        });
    }

    private static byte[] QuotedWord(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '_')
            {
                bytes.Add((byte)' ');
            }
            else if (text[i] == '=' && i + 2 < text.Length)
            {
                bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.Add((byte)text[i]);
            }
        }

        return [.. bytes];
    }

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<br\s*/?>|</p>|</div>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"=\?([^?]+)\?([BbQq])\?([^?]*)\?=")]
    private static partial Regex EncodedWord();

    [GeneratedRegex(@"\?=\s+=\?")]
    private static partial Regex AdjacentWords();
}
