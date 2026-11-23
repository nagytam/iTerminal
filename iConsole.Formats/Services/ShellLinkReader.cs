using System.Text;

namespace iConsole.Formats.Services;

/// <summary>
/// Reads a Windows shortcut (.lnk, MS-SHLLINK) into lines: the target, arguments, working folder and icon, so that a
/// shortcut can be inspected without running it. Nothing is resolved, followed or launched; the paths are text. Every
/// read is bounds-checked, so a damaged or hostile file gives a notice and not an exception.
/// </summary>
public static class ShellLinkReader
{
    private const int HeaderSize = 0x4C;
    private const uint HasLinkTargetIdList = 1u << 0;
    private const uint HasLinkInfo = 1u << 1;
    private const uint HasName = 1u << 2;
    private const uint HasRelativePath = 1u << 3;
    private const uint HasWorkingDir = 1u << 4;
    private const uint HasArguments = 1u << 5;
    private const uint HasIconLocation = 1u << 6;
    private const uint IsUnicode = 1u << 7;

    /// <summary>Programs a shortcut commonly abuses to run something other than the file it seems to open.</summary>
    private static readonly string[] ScriptHosts =
        ["cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "bitsadmin.exe", "certutil.exe", "msiexec.exe"];

    public static IReadOnlyList<string> Read(byte[] content)
    {
        try
        {
            return Describe(content);
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return [$"[Could not read the shortcut: {exception.Message}]"];
        }
    }

    private static List<string> Describe(byte[] content)
    {
        if (content.Length < HeaderSize || BitConverter.ToUInt32(content, 0) != HeaderSize)
        {
            return ["[Not a Windows shortcut]"];
        }

        uint flags = BitConverter.ToUInt32(content, 0x14);
        uint attributes = BitConverter.ToUInt32(content, 0x18);
        int showCommand = (int)BitConverter.ToUInt32(content, 0x3C);
        bool unicode = (flags & IsUnicode) != 0;
        int position = HeaderSize;

        if ((flags & HasLinkTargetIdList) != 0)
        {
            // The item list is a shell structure, not text; its two-byte size field says how far to skip.
            position += 2 + ReadUInt16(content, position);
        }

        string? target = null;
        if ((flags & HasLinkInfo) != 0)
        {
            uint size = ReadUInt32(content, position);
            target = ReadLinkInfo(content, position, size);
            position = checked(position + (int)size);
        }

        string? Text(uint flag)
        {
            if ((flags & flag) == 0)
            {
                return null;
            }

            int characters = ReadUInt16(content, position);
            position += 2;
            int bytes = unicode ? characters * 2 : characters;
            if (position + bytes > content.Length)
            {
                throw new ArgumentException("A string runs past the end of the file.");
            }

            string value = (unicode ? Encoding.Unicode : Encoding.Latin1).GetString(content, position, bytes);
            position += bytes;
            return Clean(value);
        }

        string? name = Text(HasName);
        string? relative = Text(HasRelativePath);
        string? workingDir = Text(HasWorkingDir);
        string? arguments = Text(HasArguments);
        string? icon = Text(HasIconLocation);

        List<string> lines = ["Windows shortcut (read, not run)", string.Empty];
        AddLine(lines, "Target", target);
        AddLine(lines, "Relative path", relative);
        AddLine(lines, "Arguments", arguments);
        AddLine(lines, "Working folder", workingDir);
        AddLine(lines, "Description", name);
        AddLine(lines, "Icon", icon);
        lines.Add($"Window: {ShowCommandName(showCommand)}");
        if ((attributes & 0x10) != 0)
        {
            lines.Add("Points to: a folder");
        }

        List<string> hints = Hints(target ?? relative, arguments);
        if (hints.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Hints (not a verdict):");
            lines.AddRange(hints.Select(h => "  - " + h));
        }

        return lines;
    }

    /// <summary>The local base path, or the network share and its suffix, from the LinkInfo structure.</summary>
    private static string? ReadLinkInfo(byte[] content, int start, uint size)
    {
        if (size < 0x1C || start + size > content.Length)
        {
            throw new ArgumentException("The link information is damaged.");
        }

        uint headerSize = ReadUInt32(content, start + 4);
        uint infoFlags = ReadUInt32(content, start + 8);
        string? local = null;
        if ((infoFlags & 1) != 0)
        {
            // The Unicode form of the base path exists when the header is longer than 0x1C.
            local = headerSize >= 0x24
                ? ReadNulTerminated(content, Offset(content, start, size, 0x1C), (int)(start + size), unicode: true)
                : ReadNulTerminated(content, Offset(content, start, size, 0x10), (int)(start + size), unicode: false);
        }

        string? suffix = ReadNulTerminated(content, Offset(content, start, size, 0x18), (int)(start + size), unicode: false);
        if (local is not null)
        {
            return Clean(local + suffix);
        }

        if ((infoFlags & 2) != 0)
        {
            int share = Offset(content, start, size, 0x14);
            uint netName = ReadUInt32(content, share + 8);
            if (netName >= size - (uint)(share - start))
            {
                throw new ArgumentException("The link information is damaged.");
            }

            string? name = ReadNulTerminated(content, share + (int)netName, (int)(start + size), unicode: false);
            return Clean(name + "\\" + suffix);
        }

        return null;
    }

    /// <summary>The position a LinkInfo offset field points to; an offset outside the structure marks it damaged.</summary>
    private static int Offset(byte[] content, int start, uint size, int field)
    {
        uint offset = ReadUInt32(content, start + field);
        if (offset >= size)
        {
            throw new ArgumentException("The link information is damaged.");
        }

        return start + (int)offset;
    }

    private static string? ReadNulTerminated(byte[] content, int start, int limit, bool unicode)
    {
        if (start < 0 || start >= limit || limit > content.Length)
        {
            return null;
        }

        int step = unicode ? 2 : 1;
        int end = start;
        while (end + step <= limit && (unicode ? BitConverter.ToUInt16(content, end) != 0 : content[end] != 0))
        {
            end += step;
        }

        return (unicode ? Encoding.Unicode : Encoding.Latin1).GetString(content, start, end - start);
    }

    private static List<string> Hints(string? target, string? arguments)
    {
        List<string> hints = [];
        string file = Path.GetFileName((target ?? string.Empty).Replace('\\', '/')).ToLowerInvariant();
        if (ScriptHosts.Contains(file))
        {
            hints.Add($"It starts {file}, which runs commands rather than opening a document.");
        }

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            hints.Add(arguments.Length > 260
                ? "Its arguments are very long; read them all before opening it."
                : "It passes arguments to the program it starts.");
        }

        return hints;
    }

    private static void AddLine(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            lines.Add($"{label}: {value}");
        }
    }

    private static string ShowCommandName(int command) => command switch
    {
        3 => "maximised",
        7 => "minimised",
        _ => "normal",
    };

    /// <summary>
    /// The text comes from the file and is shown on a terminal: control characters are dropped, and format characters
    /// (bidi overrides, zero-width) are shown as U+FFFD so they cannot reorder or hide what is displayed.
    /// </summary>
    private static string Clean(string text) =>
        new(text.Where(c => !char.IsControl(c))
            .Select(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? '�' : c)
            .ToArray());

    private static ushort ReadUInt16(byte[] content, int position)
    {
        if (position < 0 || position + 2 > content.Length)
        {
            throw new ArgumentException("The file ends in the middle of a field.");
        }

        return BitConverter.ToUInt16(content, position);
    }

    private static uint ReadUInt32(byte[] content, int position)
    {
        if (position < 0 || position + 4 > content.Length)
        {
            throw new ArgumentException("The file ends in the middle of a field.");
        }

        return BitConverter.ToUInt32(content, position);
    }
}
