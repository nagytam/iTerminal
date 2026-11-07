using System.Text;

namespace iTerminal.Core.Services.SystemSettings;

/// <summary>
/// The macOS user environment: a block of <c>export NAME='value'</c> lines between two marker comments in a
/// shell profile (<c>~/.zprofile</c>, which the login zsh that Terminal.app opens reads). Only that block is
/// read and rewritten; everything else in the file, its line endings and whether it ends in a newline stay as
/// they were. A line inside the block that this store did not write (a comment, a hand-written export) is kept
/// as it is. When nothing is left in the block the block goes with it.
///
/// <para>A shell that is not a login shell does not read the profile, so a change shows in the next login
/// shell, not in a running one.</para>
/// </summary>
public sealed class ProfileEnvironmentStore(string path) : IEnvironmentVariableStore
{
    internal const string BeginMarker = "# >>> iTerminal environment >>>";
    internal const string EndMarker = "# <<< iTerminal environment <<<";

    public string Path { get; } = path;

    /// <summary>Where a Mac keeps the user's login-shell profile.</summary>
    internal static string DefaultPath() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zprofile");

    public IReadOnlyList<SystemEnvironmentVariable> Read()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        var variables = new List<SystemEnvironmentVariable>();
        foreach (var line in Split(File.ReadAllText(Path)).Block)
        {
            if (TryParse(line, out var variable))
            {
                variables.RemoveAll(v => v.Name == variable.Name);
                variables.Add(variable);
            }
        }

        return [.. variables.OrderBy(v => v.Name, StringComparer.Ordinal)];
    }

    public void Set(string name, string value)
    {
        var document = Load();
        var entry = Format(name, value);
        var index = document.Block.FindIndex(line => IsVariable(line, name));
        if (index >= 0)
        {
            document.Block[index] = entry;
            document.Block.RemoveAll(line => !ReferenceEquals(line, entry) && IsVariable(line, name));
        }
        else
        {
            document.Block.Add(entry);
        }

        Write(document);
    }

    public void Remove(string name)
    {
        var document = Load();
        if (document.Block.RemoveAll(line => IsVariable(line, name)) > 0)
        {
            Write(document);
        }
    }

    internal static string Format(string name, string value) =>
        $"export {name}='{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static bool IsVariable(string line, string name) => TryParse(line, out var variable) && variable.Name == name;

    private Document Load() => File.Exists(Path) ? Split(File.ReadAllText(Path)) : new Document([], [], [], "\n", true);

    /// <summary>Cuts the file into the lines before the block, the lines in it and the lines after it.</summary>
    private static Document Split(string text)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsInNewline = text.Length == 0 || text.EndsWith('\n');
        var lines = text.Length == 0 ? [] : text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (endsInNewline && lines.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        // The block is the last begin marker before the first end marker. A begin marker that never ends (a
        // hand-edited file) is therefore left alone as ordinary text, and a new block can follow it.
        var end = lines.FindIndex(l => l == EndMarker);
        var begin = end < 0 ? -1 : lines.FindLastIndex(end, l => l == BeginMarker);

        if (begin < 0 || end < 0)
        {
            return new Document(lines, [], [], newline, endsInNewline);
        }

        return new Document(
            lines.GetRange(0, begin),
            lines.GetRange(begin + 1, end - begin - 1),
            lines.GetRange(end + 1, lines.Count - end - 1),
            newline,
            endsInNewline);
    }

    private void Write(Document document)
    {
        var lines = new List<string>(document.Before);
        if (document.Block.Count > 0)
        {
            lines.Add(BeginMarker);
            lines.AddRange(document.Block);
            lines.Add(EndMarker);
        }

        lines.AddRange(document.After);

        var text = string.Join(document.Newline, lines);
        if (lines.Count > 0 && document.EndsInNewline)
        {
            text += document.Newline;
        }

        // A profile that is a link into a dotfiles repository is written through the link, not replaced by a file.
        var target = (File.Exists(Path) ? File.ResolveLinkTarget(Path, returnFinalTarget: true)?.FullName : null) ?? Path;
        var directory = System.IO.Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Whole file or nothing: a login shell reads this at any moment.
        var temporary = target + ".tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows() && File.Exists(target))
        {
            File.SetUnixFileMode(temporary, File.GetUnixFileMode(target));
        }

        File.Move(temporary, target, overwrite: true);
    }

    /// <summary>Reads <c>export NAME='value'</c>; a line the store did not write (a comment, anything else) is not a variable.</summary>
    private static bool TryParse(string line, out SystemEnvironmentVariable variable)
    {
        variable = default!;
        const string prefix = "export ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var equals = line.IndexOf('=', prefix.Length);
        if (equals <= prefix.Length)
        {
            return false;
        }

        var name = line[prefix.Length..equals];
        var raw = line[(equals + 1)..];
        if (raw.Length < 2 || raw[0] != '\'' || raw[^1] != '\'')
        {
            return false;
        }

        variable = new SystemEnvironmentVariable(name, raw[1..^1].Replace("'\\''", "'", StringComparison.Ordinal));
        return true;
    }

    private sealed record Document(
        List<string> Before,
        List<string> Block,
        List<string> After,
        string Newline,
        bool EndsInNewline);
}
