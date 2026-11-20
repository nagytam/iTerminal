using System.Text;

namespace iTerminal.Core.Services;

/// <summary>
/// A folder list saved as one setting value: the entries joined by <c>;</c>. A value written by this codec starts with
/// <see cref="Marker"/>, and its entries have <c>;</c> and <c>%</c> escaped (<c>%3B</c>, <c>%25</c>), so an entry that
/// holds either reads back whole. A value without the marker was saved before the codec existed: it is split on
/// <c>;</c> and not unescaped, so a literal <c>%25</c> or <c>%3B</c> in an old path is read as it was written.
/// </summary>
public static class FolderListCodec
{
    private const char Separator = ';';

    // A record separator: it cannot be in a path, so no value saved before this codec starts with it.
    private const string Marker = "\u001E";

    public static string Join(IEnumerable<string> entries) =>
        Marker + string.Join(Separator, entries.Select(entry => entry.Replace("%", "%25", StringComparison.Ordinal).Replace(";", "%3B", StringComparison.Ordinal)));

    public static List<string> Split(string value)
    {
        if (!value.StartsWith(Marker, StringComparison.Ordinal))
        {
            return value.Split(Separator).ToList();
        }
        return value[Marker.Length..].Split(Separator).Select(Unescape).ToList();
    }

    private static string Unescape(string entry)
    {
        var builder = new StringBuilder(entry.Length);
        for (int i = 0; i < entry.Length; i++)
        {
            if (entry[i] == '%' && i + 2 < entry.Length)
            {
                var code = entry.Substring(i + 1, 2);
                if (code == "25")
                {
                    builder.Append('%');
                    i += 2;
                    continue;
                }
                if (code == "3B")
                {
                    builder.Append(Separator);
                    i += 2;
                    continue;
                }
            }
            builder.Append(entry[i]);
        }
        return builder.ToString();
    }
}
