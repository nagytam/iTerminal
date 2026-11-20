using System.Text;

namespace iTerminal.Core.Services;

/// <summary>
/// A folder list saved as one setting value: the entries joined by <c>;</c>. An entry that holds <c>;</c> or <c>%</c>
/// is escaped (<c>%3B</c>, <c>%25</c>) so that it reads back whole; an entry with neither is stored as it always was,
/// so a value saved before this existed reads the same.
/// </summary>
public static class FolderListCodec
{
    private const char Separator = ';';

    public static string Join(IEnumerable<string> entries) =>
        string.Join(Separator, entries.Select(entry => entry.Replace("%", "%25", StringComparison.Ordinal).Replace(";", "%3B", StringComparison.Ordinal)));

    public static List<string> Split(string value) =>
        value.Split(Separator).Select(Unescape).ToList();

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
