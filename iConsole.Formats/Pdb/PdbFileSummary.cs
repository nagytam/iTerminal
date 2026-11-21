namespace iConsole.Formats.Pdb;

/// <summary>
/// The summary the F3 viewer shows for a <c>.pdb</c> file, which is a debug symbol file in one of two formats: a portable
/// PDB (the .NET format, metadata with a <c>BSJB</c> signature) or a Windows PDB (an MSF 7.00 block container). Both are
/// binary and read only from their bytes; nothing in them is loaded, run or shown as text.
/// </summary>
public static class PdbFileSummary
{
    /// <summary>The extensions the viewer summarizes.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pdb" };

    private static ReadOnlySpan<byte> PortableSignature => "BSJB"u8;
    private static ReadOnlySpan<byte> MsfSignature => "Microsoft C/C++ MSF 7.00\r\n"u8;

    /// <summary>The lines to show. Throws <see cref="FormatException"/> when the bytes are neither format.</summary>
    public static IReadOnlyList<string> Summarize(byte[] content)
    {
        if (content.AsSpan().StartsWith(PortableSignature))
        {
            return PortablePdbReader.Summarize(content);
        }

        if (content.AsSpan().StartsWith(MsfSignature))
        {
            return MsfPdbReader.Summarize(content);
        }

        throw new FormatException("the file is neither a portable nor a Windows debug symbol file");
    }
}
