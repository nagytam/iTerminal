using System.Text.RegularExpressions;

namespace iTerminal.Core.Rename;

/// <summary>
/// The new name a marked entry gets from the Multi-rename pattern.
///
/// <para>Tokens: <c>[N]</c> is a counter from 1, <c>[N:3]</c> the same counter zero-padded to width 3,
/// <c>[C]</c> the name without its extension and <c>[E]</c> the extension without its dot. Anything
/// else in the pattern is copied as typed.</para>
///
/// <para>Find and Replace are plain text (ordinal), applied to the expanded pattern. A file whose
/// pattern does not name <c>[E]</c> keeps its extension, so <c>[C]_[N:2]</c> turns <c>a.txt</c> into
/// <c>a_01.txt</c>. A folder has no extension: its whole name is <c>[C]</c>.</para>
/// </summary>
public static class MultiRenamePattern
{
    /// <summary>The pattern a new dialog starts with: it renames nothing.</summary>
    public const string DefaultPattern = "[C]";

    private static readonly Regex TokenPattern = new(@"\[(N(?::(\d{1,3}))?|C|E)\]", RegexOptions.CultureInvariant);

    /// <summary>
    /// Splits <paramref name="name"/> into the part <c>[C]</c> names and the extension <c>[E]</c> names.
    /// A leading dot (<c>.bashrc</c>) and a trailing one (<c>a.</c>) do not start an extension.
    /// </summary>
    public static (string Stem, string Extension) Split(string name, bool isFolder)
    {
        if (isFolder)
        {
            return (name, string.Empty);
        }

        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1
            ? (name[..dot], name[(dot + 1)..])
            : (name, string.Empty);
    }

    /// <summary>The new name for <paramref name="oldName"/> at position <paramref name="counter"/> (1-based).</summary>
    public static string Expand(string pattern, string oldName, bool isFolder, int counter, string find, string replace)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(oldName);

        var (stem, extension) = Split(oldName, isFolder);
        var expanded = TokenPattern.Replace(pattern, match =>
        {
            var token = match.Groups[1].Value;
            if (token == "C")
            {
                return stem;
            }

            if (token == "E")
            {
                return extension;
            }

            var width = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
            return counter.ToString().PadLeft(width, '0');
        });

        if (!string.IsNullOrEmpty(find))
        {
            expanded = expanded.Replace(find, replace ?? string.Empty, StringComparison.Ordinal);
        }

        var keepsExtension = !isFolder && extension.Length > 0 && !pattern.Contains("[E]", StringComparison.Ordinal);
        return keepsExtension ? expanded + "." + extension : expanded;
    }
}
