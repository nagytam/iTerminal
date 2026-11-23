using System.Text;

namespace iConsole.Formats.Services;

/// <summary>
/// Reads the <c>Authentication-Results</c> header (RFC 8601) that a receiving mail server writes: the server's
/// identifier, then <c>none</c> or one <c>method=result</c> entry for each check it made (spf, dkim, dmarc …). It reads
/// what the receiver recorded and checks nothing itself, so the result is the receiver's word, and a header that
/// arrived from outside the receiving domain is only as trustworthy as its sender.
/// </summary>
public static class AuthenticationResultsReader
{
    /// <summary>The longest header read; anything beyond is ignored so a hostile message cannot cost time.</summary>
    private const int MaxLength = 4096;

    /// <summary>One check the receiver recorded.</summary>
    public sealed record Check(string Method, string Result);

    /// <summary>The server that wrote the header and the checks it recorded (empty for <c>none</c>).</summary>
    public sealed record Summary(string Server, IReadOnlyList<Check> Checks);

    /// <summary>Parses a header value; null when it holds no server identifier.</summary>
    public static Summary? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string text = StripComments(value.Length > MaxLength ? value[..MaxLength] : value);
        string[] parts = text.Split(';');
        string[] head = parts[0].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (head.Length == 0)
        {
            return null;
        }

        // The identifier may be followed by a version number, which is not a check.
        string server = head[0];
        List<Check> checks = [];
        foreach (string part in parts.Skip(1))
        {
            string[] words = part.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int equals = words[0].IndexOf('=');
            if (equals <= 0 || equals == words[0].Length - 1)
            {
                continue;
            }

            checks.Add(new Check(words[0][..equals].ToLowerInvariant(), words[0][(equals + 1)..].ToLowerInvariant()));
        }

        return new Summary(server, checks);
    }

    /// <summary>The line the e-mail viewer shows, or null when the message has no such header.</summary>
    public static string? FormatLine(string? value)
    {
        Summary? summary = Parse(value);
        if (summary is null)
        {
            return null;
        }

        string checks = summary.Checks.Count == 0
            ? "no checks recorded"
            : string.Join(", ", summary.Checks.Select(c => $"{c.Method}={c.Result}"));
        return $"Authentication-Results (as recorded by {Clean(summary.Server)}, not checked here; any sender can write this header): {checks}";
    }

    /// <summary>Removes the parenthesised comments RFC 5322 allows, which may nest.</summary>
    private static string StripComments(string text)
    {
        StringBuilder result = new(text.Length);
        int depth = 0;
        foreach (char c in text)
        {
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0)
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>The server name with control characters dropped and format characters (bidi, zero-width) shown as U+FFFD.</summary>
    private static string Clean(string text) =>
        new(text.Where(c => !char.IsControl(c))
            .Select(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? '�' : c)
            .ToArray());
}
