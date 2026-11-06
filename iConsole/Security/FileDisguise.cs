using System.Globalization;
using System.Text;

namespace iConsole.Security;

/// <summary>What a file's leading bytes say it is. Only the kinds that matter for telling a disguise are listed.</summary>
public enum FileKind
{
    Unknown,
    Executable,
    Pdf,
    Png,
    Jpeg,
    Gif,
    Zip,
}

/// <summary>The ways a file name or a file's content can pretend to be something it is not.</summary>
[Flags]
public enum DisguiseFindings
{
    None = 0,

    /// <summary>A document extension followed by a launchable one: <c>invoice.pdf.exe</c>.</summary>
    DoubleExtension = 1,

    /// <summary>A right-to-left override, another bidi control, a zero-width or other control character in the name.</summary>
    HiddenCharacters = 2,

    /// <summary>The leading bytes are a different kind of file than the extension claims.</summary>
    TypeMismatch = 4,
}

/// <summary>
/// Spots files that are not what their name says: an executable called <c>.pdf</c>, <c>invoice.pdf.exe</c>, a name
/// that uses U+202E to show its extension backwards. Everything here is pure; the one read of a file's first
/// bytes belongs to the caller, on a background task, never in a key handler or Draw.
/// </summary>
public static class FileDisguise
{
    /// <summary>How many leading bytes <see cref="Detect"/> needs.</summary>
    public const int HeaderLength = 8;

    /// <summary>Extensions that run something when opened: what the second extension of a double one is.</summary>
    private static readonly HashSet<string> LaunchExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "scr", "com", "pif", "bat", "cmd", "ps1", "vbs", "vbe", "js", "jse", "wsf", "wsh", "lnk",
        "msi", "jar", "hta", "cpl", "reg", "sh", "appimage",
    };

    /// <summary>Extensions that promise plain data: opening one is not thought to run anything.</summary>
    private static readonly HashSet<string> DataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "doc", "docx", "docm", "xls", "xlsx", "xlsm", "ppt", "pptx", "rtf", "odt", "ods", "odp", "txt",
        "csv", "tsv", "json", "xml", "html", "htm", "md", "log", "jpg", "jpeg", "png", "gif", "bmp", "webp",
        "tif", "tiff", "ico", "svg", "mp3", "wav", "flac", "ogg", "mp4", "mkv", "avi", "mov", "zip", "7z", "rar",
    };

    /// <summary>The kind the leading bytes show, or <see cref="FileKind.Unknown"/>.</summary>
    public static FileKind Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 2 && header[0] == 0x4D && header[1] == 0x5A)
        {
            return FileKind.Executable; // MZ: Windows PE (and DOS)
        }

        if (header.Length >= 4 && header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46)
        {
            return FileKind.Executable; // ELF
        }

        if (header.Length >= 4)
        {
            uint magic = (uint)header[0] << 24 | (uint)header[1] << 16 | (uint)header[2] << 8 | header[3];
            // Mach-O, both widths and byte orders. 0xCAFEBABE is a fat binary and a Java class: left out.
            if (magic is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE)
            {
                return FileKind.Executable;
            }
        }

        if (header.Length >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D)
        {
            return FileKind.Pdf;
        }

        if (header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return FileKind.Png;
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return FileKind.Jpeg;
        }

        if (header.Length >= 4 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
        {
            return FileKind.Gif;
        }

        if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
        {
            return FileKind.Zip;
        }

        return FileKind.Unknown;
    }

    /// <summary>The kind a file name's extension promises, where it promises one that can be checked.</summary>
    public static FileKind Claimed(string name) => ExtensionOf(name).ToLowerInvariant() switch
    {
        "pdf" => FileKind.Pdf,
        "png" => FileKind.Png,
        "jpg" or "jpeg" => FileKind.Jpeg,
        "gif" => FileKind.Gif,
        "zip" or "docx" or "xlsx" or "pptx" or "odt" or "ods" or "odp" => FileKind.Zip,
        _ => FileKind.Unknown,
    };

    /// <summary>
    /// True when <paramref name="actual"/> contradicts the name. An executable under an extension that promises
    /// data is one; so is a PDF, PNG, JPEG, GIF or ZIP whose bytes are another of those. Unknown content is never
    /// a mismatch (plain text has no signature), and neither is an executable under an extension that does not
    /// promise data (no extension, <c>.exe</c>, <c>.so</c>, a version number).
    /// </summary>
    public static bool IsMismatch(string name, FileKind actual)
    {
        if (actual == FileKind.Unknown)
        {
            return false;
        }

        if (actual == FileKind.Executable)
        {
            return DataExtensions.Contains(ExtensionOf(name));
        }

        var claimed = Claimed(name);
        return claimed != FileKind.Unknown && claimed != actual;
    }

    /// <summary>The findings that need only the name: a double extension and hidden characters.</summary>
    public static DisguiseFindings AnalyzeName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return DisguiseFindings.None;
        }

        var findings = DisguiseFindings.None;
        if (name.Any(IsHiddenCharacter))
        {
            findings |= DisguiseFindings.HiddenCharacters;
        }

        if (DoubleExtension(name) is not null)
        {
            findings |= DisguiseFindings.DoubleExtension;
        }

        return findings;
    }

    /// <summary>The two extensions of <c>invoice.pdf.exe</c> as <c>("pdf", "exe")</c>, or null.</summary>
    public static (string Shown, string Real)? DoubleExtension(string name)
    {
        var last = name.LastIndexOf('.');
        if (last <= 0)
        {
            return null;
        }

        var previous = name.LastIndexOf('.', last - 1);
        if (previous < 0)
        {
            return null;
        }

        var real = name[(last + 1)..];
        var shown = name[(previous + 1)..last];
        return LaunchExtensions.Contains(real) && DataExtensions.Contains(shown) ? (shown, real) : null;
    }

    /// <summary>
    /// A character that changes how a name reads or does not show at all: the bidi embeddings, overrides and
    /// isolates, the direction marks, the zero-width characters, and every control character.
    /// </summary>
    public static bool IsHiddenCharacter(char c) =>
        char.IsControl(c)
        || c is (char)0x061C or (char)0x200B or (char)0x200C or (char)0x200D or (char)0x200E or (char)0x200F
            or (char)0x2060 or (char)0xFEFF or (char)0x2028 or (char)0x2029
        || c is >= (char)0x202A and <= (char)0x202E
        || c is >= (char)0x2066 and <= (char)0x2069;

    /// <summary>The name with every hidden character spelled out as <c>&lt;U+202E&gt;</c>, for a place that must show them.</summary>
    public static string Reveal(string name)
    {
        var text = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (IsHiddenCharacter(c))
            {
                text.Append("<U+").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)).Append('>');
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    /// <summary>The name with every hidden character shown as a middle dot, so a listing cannot be bent by it.</summary>
    public static string Mask(string name)
    {
        if (!name.Any(IsHiddenCharacter))
        {
            return name;
        }

        return string.Create(name.Length, name, static (span, source) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = IsHiddenCharacter(source[i]) ? (char)0x00B7 : source[i];
            }
        });
    }

    /// <summary>A short name for a kind, for a sentence.</summary>
    public static string KindName(FileKind kind) => kind switch
    {
        FileKind.Executable => "an executable",
        FileKind.Pdf => "a PDF",
        FileKind.Png => "a PNG image",
        FileKind.Jpeg => "a JPEG image",
        FileKind.Gif => "a GIF image",
        FileKind.Zip => "a ZIP archive",
        _ => "unknown",
    };

    /// <summary>The extension without its dot; empty for none (a leading dot is a hidden file, not an extension).</summary>
    public static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? string.Empty : name[(dot + 1)..];
    }
}
