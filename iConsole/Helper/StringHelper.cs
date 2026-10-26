using System.Text;

namespace iConsole.Helper;

public static class StringHelper
{
    public static string Repeat(this string value, int count)
    {
        if (count > 0)
        {
            return new StringBuilder().Insert(0, value, count).ToString();
        }
        else
        {
            return string.Empty;
        }
    }

    public static string Repeat(char c, int count)
    {
        if (count <= 0) return string.Empty;
        return new StringBuilder().Insert(0, c.ToString(), count).ToString();
    }

    public static string Cut(this string value, int size)
    {
        if (size <= 0) return string.Empty;
        return (value.Length > size) ? value[..size] : value;
    }

    /// <summary>
    /// Breaks text into display lines of at most <paramref name="width"/> characters, at word
    /// boundaries where there is one and mid-word where there is not — a long URL or a base64 blob
    /// must not produce one line a thousand characters long that the control then clips to nothing.
    /// Line breaks in the text are kept (a blank line stays a blank line); <c>\r</c> is dropped.
    /// </summary>
    public static List<string> WrapWords(this string? text, int width)
    {
        List<string> lines = [];
        if (width <= 0 || string.IsNullOrEmpty(text))
        {
            return lines;
        }

        foreach (string paragraph in text.Split('\n'))
        {
            string remaining = paragraph.TrimEnd();

            if (remaining.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            while (remaining.Length > width)
            {
                int breakAt = remaining.LastIndexOf(' ', Math.Min(width, remaining.Length - 1));
                if (breakAt <= 0)
                {
                    breakAt = width;
                }

                lines.Add(remaining[..breakAt].TrimEnd());
                remaining = remaining[breakAt..].TrimStart();
            }

            lines.Add(remaining);
        }

        return lines;
    }

    /// <summary>
    /// A row of key hints ("Enter:open  r:rescan  Esc:close") made to fit <paramref name="width"/> by
    /// leaving out whole hints rather than cutting one in half: the hints are separated by two or more
    /// spaces, and they go from the end — except the last, which by convention says how to leave
    /// ("Esc:close") and is the one hint a user who is stuck needs. A row cut at the edge used to end
    /// in "Esc:clos" or "Es".
    /// </summary>
    public static string FitHints(this string? hints, int width)
    {
        if (string.IsNullOrEmpty(hints) || width <= 0)
        {
            return string.Empty;
        }
        if (hints.Length <= width)
        {
            return hints;
        }
        var leading = hints.Length - hints.TrimStart(' ').Length;
        var items = System.Text.RegularExpressions.Regex.Split(hints.Trim(), @"\s{2,}").ToList();
        var prefix = new string(' ', Math.Min(leading, 1));
        string Join() => prefix + string.Join("  ", items);
        while (items.Count > 1 && Join().Length > width)
        {
            items.RemoveAt(items.Count - 2);
        }
        var fitted = Join();
        return fitted.Length <= width ? fitted : fitted.Cut(width);
    }

    public static string Expand(this string value, int left, int right)
    {
        var paddedString = " ".Repeat(left) + value + " ".Repeat(right);
        return paddedString;
    }

    public static string Expand(this string value, int width)
    {
        return Expand(value, " ", width);
    }

    public static string Expand(this string value, string c, int width)
    {
        int left = (width - value.Length) / 2;
        int right = (width - value.Length) / 2;
        if (left + value.Length + right < width)
        {
            right++;
        }
        var paddedString = c.Repeat(left) + value + c.Repeat(right);
        return paddedString;
    }

    public static string ExpandRight(this string? value, int width)
    {
        if (value is null)
        {
            return string.Empty;
        }

        int right = (width - value.Length);
        var paddedString = value + " ".Repeat(right);
        return paddedString;
    }

    public static string? Shrink(this string? s, int maxLength)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxLength)
        {
            return s;
        }
        else
        {
            maxLength = Math.Max(0, maxLength);
            var shrinkedSectionLength = (maxLength - 3) / 2;
            if (shrinkedSectionLength >= 0 && shrinkedSectionLength < s.Length)
            {
                var prefix = s[..shrinkedSectionLength];
                var postfix = s[(s.Length - shrinkedSectionLength)..];
                return prefix + "..." + postfix;
            }
            else
            {
                return s[..maxLength];
            }
        }
    }

    public static string AlignCenter(string? line, string? title)
    {
        line ??= string.Empty;
        title ??= string.Empty;
        var newLine = line;
        if (title.Length < line.Length)
        {
            var start = (line.Length - title.Length) / 2;
            newLine = string.Concat(line.AsSpan(0, start), title, line.AsSpan(start + title.Length));
        }
        return newLine;
    }

    public static MemoryStream ToMemoryStream(this string s, Encoding encoding)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream, encoding);
        writer.Write(s);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }
}
