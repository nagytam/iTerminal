namespace iConsole.Executables;

/// <summary>Which files the analysis view opens: the endings worth probing, and the magic bytes that tell an executable from anything else.</summary>
public static class ExecutableFileTypes
{
    /// <summary>The endings probed for a signature. A file with no ending at all is probed too (Linux programs have none).</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".ocx", ".scr", ".cpl", ".efi", ".drv", ".mui", ".winmd",
            ".so", ".ko", ".o", ".elf", ".axf",
            ".dylib", ".bundle", ".kext",
        };

    /// <summary>How many bytes of the start the magic check needs.</summary>
    public const int HeaderLength = 8;

    /// <summary>True when the name is one the view probes: a known ending, a versioned <c>.so.1</c> name, or no ending.</summary>
    public static bool IsCandidate(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        string name = Path.GetFileName(path);
        string extension = Path.GetExtension(name);
        return extension.Length == 0
            || Extensions.Contains(extension)
            || name.Contains(".so.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the first bytes are the start of a PE ("MZ"), an ELF (0x7F "ELF") or a Mach-O (either byte order, 32 or 64 bit, or
    /// a universal binary with 1 to 30 slices; a Java class is not one).
    /// </summary>
    public static bool HasSignature(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 'M' && bytes[1] == 'Z')
        {
            return true;
        }

        if (bytes.Length < 4)
        {
            return false;
        }

        if (bytes[0] == 0x7F && bytes[1] == 'E' && bytes[2] == 'L' && bytes[3] == 'F')
        {
            return true;
        }

        uint big = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        if (big is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE)
        {
            return true;
        }

        // A universal binary and a Java class share 0xCAFEBABE; a class has its version (45 or more) where a universal binary has a
        // count of slices, so a small count is what tells them apart.
        if (big == 0xCAFEBABE && bytes.Length >= 8)
        {
            uint slices = (uint)(bytes[4] << 24 | bytes[5] << 16 | bytes[6] << 8 | bytes[7]);
            return slices is >= 1 and <= 30;
        }

        return false;
    }

    /// <summary>Reads the first bytes of a local file and checks them. Blocking I/O: call it from a worker thread. An unreadable file is no executable.</summary>
    public static bool FileHasSignature(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] buffer = new byte[HeaderLength];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return HasSignature(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
