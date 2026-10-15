using iConsole.Interfaces;
using iTerminal.Core.Services.Search;
using iTerminal.Core.Services.Shell;
using System.Text;

namespace iTerminal.Core.Services.UserMenu;

/// <summary>One entry of the user menu: a hot key, a title, the files it is offered for and the script.</summary>
/// <param name="Hotkey">The key that runs it from the menu; a blank for none.</param>
/// <param name="Patterns">The names it is offered for, in F7's syntax (<c>*.c;*.h</c>); <c>*</c> for every file.</param>
/// <param name="Lines">The script, one command per line, with the <c>%</c> macros of <see cref="UserMenuMacros"/> still in it.</param>
public sealed record UserMenuItem(char Hotkey, string Title, string Patterns, IReadOnlyList<string> Lines)
{
    public bool AppliesTo(string? fileName) =>
        Patterns is "*" or ""
        || (!string.IsNullOrEmpty(fileName) && FileSearch.MatchesName(fileName, FileSearch.ParsePatterns(Patterns)));
}

/// <summary>What the macros of a menu entry stand for: the file under the cursor, its folder, the marks.</summary>
/// <param name="CurrentFile">Name of the entry under the cursor, or empty.</param>
/// <param name="Folder">The local folder the panel shows.</param>
/// <param name="OtherFolder">The other panel's folder, when it is a local one.</param>
/// <param name="MarkedFiles">Names of the marked entries; empty when nothing is marked.</param>
public sealed record UserMenuContext(string CurrentFile, string Folder, string OtherFolder, IReadOnlyList<string> MarkedFiles);

/// <summary>
/// Expands mc's menu macros: <c>%f</c> the file's name, <c>%b</c> without its extension, <c>%x</c>
/// the extension, <c>%d</c> the folder, <c>%D</c> the other panel's folder, <c>%s</c> the marked
/// files (or the current one when nothing is marked), <c>%t</c> the marked files only, <c>%%</c> a
/// percent sign. Names are quoted for the shell the script runs in, so a file with a space or a
/// quote in its name stays one argument.
/// </summary>
public static class UserMenuMacros
{
    public static string Expand(string line, UserMenuContext context)
    {
        var result = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '%' || i + 1 >= line.Length)
            {
                result.Append(line[i]);
                continue;
            }

            var next = line[++i];
            switch (next)
            {
                case 'f':
                    result.Append(Quote(context.CurrentFile));
                    break;
                case 'b':
                    result.Append(Quote(Path.GetFileNameWithoutExtension(context.CurrentFile)));
                    break;
                case 'x':
                    result.Append(Path.GetExtension(context.CurrentFile).TrimStart('.'));
                    break;
                case 'd':
                    result.Append(Quote(context.Folder));
                    break;
                case 'D':
                    result.Append(Quote(context.OtherFolder));
                    break;
                case 's':
                    result.Append(string.Join(' ', (context.MarkedFiles.Count > 0 ? context.MarkedFiles : [context.CurrentFile]).Select(Quote)));
                    break;
                case 't':
                    result.Append(string.Join(' ', context.MarkedFiles.Select(Quote)));
                    break;
                case '%':
                    result.Append('%');
                    break;
                default:
                    result.Append('%').Append(next);
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>Quotes one word for cmd on Windows and for sh elsewhere.</summary>
    public static string Quote(string word)
    {
        if (OperatingSystem.IsWindows())
        {
            return "\"" + word.Replace("\"", "\"\"") + "\"";
        }

        return "'" + word.Replace("'", "'\\''") + "'";
    }
}

/// <summary>
/// Reads the menu file, in the spirit of <c>mc.menu</c>: a line that starts in the first column
/// opens an entry — its hot key, a blank, the title — and the indented lines after it are its
/// script. A <c>+ f &lt;patterns&gt;</c> line before an entry offers it only for matching file
/// names. Lines starting with <c>#</c> are comments.
/// </summary>
public static class UserMenuParser
{
    public static IReadOnlyList<UserMenuItem> Parse(string text)
    {
        var items = new List<UserMenuItem>();
        var patterns = "*";
        var hotkey = ' ';
        string? title = null;
        var lines = new List<string>();

        void Flush()
        {
            if (title is not null)
            {
                items.Add(new UserMenuItem(hotkey, title, patterns, [.. lines]));
                patterns = "*";
            }

            title = null;
            lines.Clear();
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (char.IsWhiteSpace(line[0]))
            {
                if (title is not null)
                {
                    lines.Add(line.Trim());
                }

                continue;
            }

            if (line[0] is '+' or '=')
            {
                Flush();
                var condition = line[1..].Trim();
                if (condition.StartsWith("f ", StringComparison.Ordinal))
                {
                    patterns = condition[2..].Trim();
                }

                continue;
            }

            Flush();
            var space = line.IndexOfAny([' ', '\t']);
            if (space == 1)
            {
                hotkey = line[0];
                title = line[2..].Trim();
            }
            else
            {
                hotkey = ' ';
                title = line.Trim();
            }
        }

        Flush();
        return items;
    }
}

public interface IUserMenuService
{
    /// <summary>The menu file, so a person can be pointed at it.</summary>
    string FilePath { get; }

    /// <summary>The entries offered for <paramref name="fileName"/>, in file order. Reads the file on every call.</summary>
    IReadOnlyList<UserMenuItem> GetItems(string? fileName);

    /// <summary>Runs the entry on the whole terminal and completes when it is over. The result is null when it ran, else why it did not.</summary>
    Task<string?> RunAsync(UserMenuItem item, UserMenuContext context, CancellationToken cancellationToken = default);
}

public class UserMenuService(string filePath, IInteractiveShellService shellService, IContentModelService contentModelService) : IUserMenuService
{
    public const string FileName = "usermenu.txt";

    public static string DefaultText { get; } = string.Join('\n',
        "# iTerminal user menu (Shift+F10) - the same idea as mc's F2 menu.",
        "# An entry: a key, a blank, a title; then its commands, indented.",
        "# \"+ f <patterns>\" before an entry offers it only for matching files.",
        "# Macros: %f file  %b name without extension  %x extension  %d folder",
        "#         %D other panel's folder  %s marked files (or the file)  %t marked files  %%",
        "",
        "l       List the file's lines",
        "    wc -l %f",
        "",
        "+ f *.tar.gz;*.tgz",
        "t       Unpack with tar",
        "    tar -xzf %f",
        "",
        "g       Show git status of the folder",
        "    git status",
        "",
        "d       Disk usage of the folder",
        "    du -sh %d",
        "");

    public string FilePath => filePath;

    public IReadOnlyList<UserMenuItem> GetItems(string? fileName)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                var folder = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.WriteAllText(filePath, DefaultText.Replace("\n", Environment.NewLine));
            }

            return [.. UserMenuParser.Parse(File.ReadAllText(filePath)).Where(i => i.AppliesTo(fileName))];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public async Task<string?> RunAsync(UserMenuItem item, UserMenuContext context, CancellationToken cancellationToken = default)
    {
        var script = Path.Combine(
            Path.GetTempPath(),
            "iterminal-menu-" + Guid.NewGuid().ToString("N") + (OperatingSystem.IsWindows() ? ".cmd" : ".sh"));
        try
        {
            var lines = item.Lines.Select(l => UserMenuMacros.Expand(l, context)).ToList();
            var body = new StringBuilder();
            if (OperatingSystem.IsWindows())
            {
                body.Append("@echo off\r\n");
                lines.ForEach(l => body.Append(l).Append("\r\n"));
                body.Append("echo.\r\necho Press any key to return . . .\r\npause >nul\r\n");
            }
            else
            {
                lines.ForEach(l => body.Append(l).Append('\n'));
                body.Append("printf '\\nPress Enter to return . . . '\nread _\n");
            }

            await File.WriteAllTextAsync(script, body.ToString(), cancellationToken);

            var folder = Directory.Exists(context.Folder) ? context.Folder : null;
            var launch = OperatingSystem.IsWindows()
                ? new ShellLaunch(Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec ? comSpec : "cmd.exe", ["/c", script], folder, context.Folder)
                : new ShellLaunch("/bin/sh", [script], folder, context.Folder);
            var outcome = await shellService.RunAsync(launch with { Banner = string.Empty }, cancellationToken);

            // Whatever the script did to the folder is unknown, and only the local drive notices
            // its own changes — the same reason Ctrl+O re-reads the panels.
            foreach (var model in contentModelService.ContentModels.ToList())
            {
                try
                {
                    model.Refresh(forceRefresh: true);
                }
                catch
                {
                    // One panel that cannot be re-read must not keep the other from it.
                }
            }

            return outcome.Started ? null : outcome.Failure ?? $"{item.Title} did not start.";
        }
        finally
        {
            try
            {
                File.Delete(script);
            }
            catch (IOException)
            {
            }
        }
    }
}
