using iTerminal.Core.Services.Search;

namespace iTerminal.Core.Services.FileAssociations;

/// <summary>What an association is for: Enter ("open") or F4 ("edit").</summary>
public enum FileAssociationKind
{
    Open,
    Edit,
}

/// <summary>
/// A program the user has chosen for a kind of file: which names it applies to, the command that
/// starts it, and whether it needs the terminal.
/// </summary>
/// <param name="Id">Unique among the saved associations.</param>
/// <param name="Name">What the "Open with" list shows.</param>
/// <param name="Patterns">The names it applies to, in F7's syntax: <c>*.jpg;*.png</c>, <c>*</c> for every file.</param>
/// <param name="Command">
/// The command line, with <c>%f</c> for the file, <c>%d</c> for its folder, <c>%n</c> for its name
/// and <c>%b</c> for the name without its extension; see <see cref="BuildCommandLine"/>.
/// </param>
/// <param name="InTerminal">
/// True for a program that draws in the terminal (vim, nano, less): it is lent the whole screen and
/// the panels come back when it exits. False for one that opens a window of its own.
/// </param>
public sealed record FileAssociation(
    string Id,
    string Name,
    string Patterns,
    string Command,
    FileAssociationKind Kind,
    bool InTerminal)
{
    public bool Matches(string fileName) =>
        !string.IsNullOrEmpty(fileName) && FileSearch.MatchesName(fileName, FileSearch.ParsePatterns(Patterns));

    /// <summary>The pattern a new association for <paramref name="fileName"/> starts with: its extension, or the whole name when it has none.</summary>
    public static string PatternFor(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length > 1 ? "*" + extension.ToLowerInvariant() : fileName;
    }

    /// <summary>
    /// The program and its arguments for opening <paramref name="filePath"/> with
    /// <paramref name="command"/>, or null when the command is empty.
    ///
    /// <para>The command is split into words first — at blanks, with double quotes keeping a word
    /// together and no escape character, since a Windows path is full of backslashes — and the
    /// placeholders are replaced inside each word afterwards. So a path with spaces in it stays one
    /// argument without the user having to quote <c>%f</c>, and nothing in a file name can end up
    /// read as a second argument or an option of the shell: no shell is involved at all. A command
    /// that names the file nowhere gets it as its last argument, which is what typing just
    /// <c>notepad</c> means.</para>
    /// </summary>
    public static (string FileName, IReadOnlyList<string> Arguments)? BuildCommandLine(string? command, string filePath)
    {
        var words = SplitWords(command);
        if (words.Count == 0)
        {
            return null;
        }

        var namesTheFile = false;
        var arguments = new List<string>();
        foreach (var word in words.Skip(1))
        {
            arguments.Add(Expand(word, filePath, ref namesTheFile));
        }

        if (!namesTheFile)
        {
            arguments.Add(filePath);
        }

        return (words[0], arguments);
    }

    internal static List<string> SplitWords(string? command)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        var inQuotes = false;
        var hasWord = false;
        foreach (var c in command ?? string.Empty)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasWord = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasWord)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    hasWord = false;
                }
            }
            else
            {
                word.Append(c);
                hasWord = true;
            }
        }

        if (hasWord)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    private static string Expand(string word, string filePath, ref bool namesTheFile)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < word.Length; i++)
        {
            if (word[i] != '%' || i + 1 >= word.Length)
            {
                result.Append(word[i]);
                continue;
            }

            var next = word[++i];
            switch (next)
            {
                case 'f':
                    result.Append(filePath);
                    namesTheFile = true;
                    break;
                case 'd':
                    result.Append(Path.GetDirectoryName(filePath) ?? string.Empty);
                    break;
                case 'n':
                    result.Append(Path.GetFileName(filePath));
                    namesTheFile = true;
                    break;
                case 'b':
                    result.Append(Path.GetFileNameWithoutExtension(filePath));
                    break;
                case '%':
                    result.Append('%');
                    break;
                default:
                    // Not a placeholder: %PATH%-style text, or a percent sign in a file name.
                    result.Append('%').Append(next);
                    break;
            }
        }

        return result.ToString();
    }
}
