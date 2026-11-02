using System.Text;

namespace iConsole.Services;

/// <summary>One property of an iCalendar or vCard file: a name, its parameters and the raw, still escaped value.</summary>
internal readonly record struct ContentLine(string Name, IReadOnlyList<(string Key, string Value)> Parameters, string Value)
{
    /// <summary>The first value of the named parameter, or null when the property does not carry it.</summary>
    public string? Parameter(string key)
    {
        foreach ((string name, string value) in Parameters)
        {
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>All the values of a parameter, each comma-separated item on its own (TYPE=WORK,VOICE gives two).</summary>
    public IEnumerable<string> ParameterValues(string key) =>
        Parameters.Where(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>
/// The line format shared by iCalendar (RFC 5545) and vCard (RFC 6350): text split into lines, folded lines joined back
/// together, and each line taken apart into its name, parameters and value. The value is returned as written, so a
/// caller that wants the text unescapes it with <see cref="Unescape"/>, and one that wants the parts of a structured
/// value (a vCard name or address) splits it with <see cref="SplitStructured"/>.
/// </summary>
internal static class ContentLines
{
    /// <summary>Splits <paramref name="text"/> into logical lines: CRLF or LF ends a line, and a line that begins with a space or tab continues the previous one.</summary>
    public static List<string> Unfold(string text)
    {
        List<string> lines = [];
        foreach (string physical in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (physical.Length > 0 && (physical[0] == ' ' || physical[0] == '\t') && lines.Count > 0)
            {
                lines[^1] += physical[1..];
            }
            else
            {
                lines.Add(physical);
            }
        }

        return lines;
    }

    /// <summary>Takes a logical line apart; null for a blank line, or one with no name before its separator.</summary>
    public static ContentLine? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        // The name runs to the first ';' (parameters follow) or ':' (the value follows).
        int nameEnd = line.IndexOfAny([';', ':']);
        if (nameEnd <= 0)
        {
            return null;
        }

        string name = line[..nameEnd].Trim();
        if (line[nameEnd] == ':')
        {
            return new ContentLine(name, [], line[(nameEnd + 1)..]);
        }

        // The parameters run to the first ':' outside quotes, because a quoted parameter may hold a colon.
        bool quoted = false;
        int colon = -1;
        for (int i = nameEnd + 1; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && line[i] == ':')
            {
                colon = i;
                break;
            }
        }

        if (colon < 0)
        {
            return new ContentLine(name, [], string.Empty);
        }

        return new ContentLine(name, ParseParameters(line[(nameEnd + 1)..colon]), line[(colon + 1)..]);
    }

    /// <summary>Adds a property's value under its label; a value with line breaks (a description) continues on indented lines.</summary>
    public static void AppendValue(List<string> lines, string label, string continuation, string value)
    {
        string[] parts = value.Split('\n');
        lines.Add(label + parts[0]);
        foreach (string part in parts.Skip(1))
        {
            lines.Add(continuation + part);
        }
    }

    /// <summary>Undoes the escapes of a text value: \n and \N are line breaks, \, \; and \\ are themselves.</summary>
    public static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0)
        {
            return value;
        }

        StringBuilder builder = new(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                char next = value[++i];
                builder.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Splits a structured value (N, ADR, ORG, CATEGORIES) on unescaped <paramref name="separator"/>s, unescaping each part.</summary>
    public static List<string> SplitStructured(string value, char separator)
    {
        List<string> parts = [];
        StringBuilder current = new();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                current.Append(c).Append(value[++i]);
            }
            else if (c == separator)
            {
                parts.Add(Unescape(current.ToString()));
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(Unescape(current.ToString()));
        return parts;
    }

    private static List<(string Key, string Value)> ParseParameters(string text)
    {
        List<(string Key, string Value)> parameters = [];
        foreach (string part in SplitOutsideQuotes(text, ';'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            int equals = part.IndexOf('=');
            if (equals < 0)
            {
                // A bare value is a vCard 2.1 type, such as TEL;WORK or ADR;HOME.
                parameters.Add(("TYPE", part.Trim('"')));
                continue;
            }

            parameters.Add((part[..equals].Trim(), part[(equals + 1)..].Trim('"')));
        }

        return parameters;
    }

    private static IEnumerable<string> SplitOutsideQuotes(string text, char separator)
    {
        bool quoted = false;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && text[i] == separator)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        yield return text[start..];
    }
}
