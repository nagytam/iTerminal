using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;

namespace iConsole.Pdb;

/// <summary>
/// The summary of a portable PDB: the debug metadata header (its id and the entry point), the source documents, and the
/// methods that have sequence points. Read with <see cref="MetadataReader"/>, the same reader the IL disassembler uses.
/// </summary>
internal static class PortablePdbReader
{
    private const int MaxListed = 20;

    public static IReadOnlyList<string> Summarize(byte[] content)
    {
        ImmutableArray<byte> image = ImmutableCollectionsMarshal.AsImmutableArray(content);
        try
        {
            using var provider = MetadataReaderProvider.FromPortablePdbImage(image);
            return Lines(provider.GetMetadataReader());
        }
        catch (BadImageFormatException exception)
        {
            throw new FormatException("the portable PDB is malformed", exception);
        }
    }

    private static List<string> Lines(MetadataReader reader)
    {
        var lines = new List<string> { "Debug symbols (portable PDB)" };
        lines.Add($"Metadata version: {reader.MetadataVersion}");

        DebugMetadataHeader? header = reader.DebugMetadataHeader;
        if (header is not null)
        {
            ImmutableArray<byte> id = header.Id;
            if (id.Length == 20)
            {
                var guid = new Guid(id.AsSpan(0, 16));
                uint stamp = BitConverter.ToUInt32(id.AsSpan(16, 4));
                lines.Add($"Id: {guid:D} (stamp {stamp:X8})");
            }

            lines.Add($"Entry point: {EntryPoint(header.EntryPoint)}");
        }

        int documents = reader.GetTableRowCount(TableIndex.Document);
        lines.Add($"Documents: {documents}");
        int listed = 0;
        foreach (DocumentHandle handle in reader.Documents)
        {
            if (listed++ == MaxListed)
            {
                lines.Add($"  ... and {documents - MaxListed} more");
                break;
            }

            lines.Add("  " + DocumentName(reader, handle));
        }

        int withSequencePoints = 0;
        foreach (MethodDebugInformationHandle handle in reader.MethodDebugInformation)
        {
            if (!reader.GetMethodDebugInformation(handle).SequencePointsBlob.IsNil)
            {
                withSequencePoints++;
            }
        }

        lines.Add($"Methods with sequence points: {withSequencePoints} of {reader.GetTableRowCount(TableIndex.MethodDebugInformation)}");
        return lines;
    }

    /// <remarks>
    /// A portable PDB holds only the debug tables; the method's name and type are in the assembly's metadata, which this file
    /// does not contain. So the entry point is shown as its token, which is what the assembly's metadata would name.
    /// </remarks>
    private static string EntryPoint(MethodDefinitionHandle handle) =>
        handle.IsNil
            ? "none"
            : $"method token 0x{MetadataTokens.GetToken(handle):X8} (its name is in the assembly)";

    /// <summary>A document name is a separator byte and a list of name parts, each a blob of its own.</summary>
    private static string DocumentName(MetadataReader reader, DocumentHandle handle)
    {
        Document document = reader.GetDocument(handle);
        if (document.Name.IsNil)
        {
            return string.Empty;
        }

        BlobReader blob = reader.GetBlobReader(document.Name);
        byte separator = blob.ReadByte();
        string joiner = separator == 0 ? string.Empty : ((char)separator).ToString(CultureInfo.InvariantCulture);
        var parts = new List<string>();
        while (blob.RemainingBytes > 0)
        {
            BlobReader part = reader.GetBlobReader(MetadataTokens.BlobHandle(blob.ReadCompressedInteger()));
            parts.Add(part.ReadUTF8(part.Length));
        }

        return string.Join(joiner, parts);
    }
}
