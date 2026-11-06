using System.Text;
using iConsole.Executables;

namespace iTerminal.Core.Services.Executables;

/// <summary>
/// Reads a Mach-O file by hand: a thin file, or the first readable slice of a universal binary. The load commands are walked inside one
/// bounded block, every command size is checked against it, and the counts are capped.
/// </summary>
internal static class MachOAnalyzer
{
    private const int MaxLoadCommandBytes = 1024 * 1024;
    private const int MaxLoadCommands = 4096;
    private const int MaxSections = 2048;
    private const int MaxSymbols = 1_000_000;
    private const int MaxExportNames = 25;
    private const int MaxLibraries = 128;
    private const int MaxStringTable = 4 * 1024 * 1024;
    private const int MaxSlices = 30;

    public static ExecutableReport Analyze(BoundedFileReader file, CancellationToken cancellationToken)
    {
        byte[] magic = new byte[8];
        if (file.Length < 8 || !file.TryRead(0, magic))
        {
            throw new ExecutableAnalysisException("The file is too short for a Mach-O header.");
        }

        if (BoundedFileReader.U32(magic, 0, true) != 0xCAFEBABE)
        {
            return AnalyzeSlice(file, 0, file.Length, null, cancellationToken);
        }

        uint slices = BoundedFileReader.U32(magic, 4, true);
        if (slices is < 1 or > MaxSlices)
        {
            throw new ExecutableAnalysisException("The universal binary header is not valid.");
        }

        byte[]? table = file.ReadBytes(8, slices * 20L, MaxSlices * 20);
        if (table is null)
        {
            throw new ExecutableAnalysisException("The universal binary header is cut short.");
        }

        List<string> architectures = [];
        (long Offset, long Size)? first = null;
        for (int i = 0; i < slices; i++)
        {
            uint cpu = BoundedFileReader.U32(table, i * 20, true);
            uint offset = BoundedFileReader.U32(table, i * 20 + 8, true);
            uint size = BoundedFileReader.U32(table, i * 20 + 12, true);
            architectures.Add(CpuName(cpu));
            if (first is null && size >= 28 && file.InRange(offset, size))
            {
                first = (offset, size);
            }
        }

        if (first is null)
        {
            throw new ExecutableAnalysisException("No slice of the universal binary lies inside the file.");
        }

        return AnalyzeSlice(file, first.Value.Offset, first.Value.Size, architectures, cancellationToken);
    }

    private static ExecutableReport AnalyzeSlice(BoundedFileReader file, long baseOffset, long sliceLength, List<string>? universal, CancellationToken cancellationToken)
    {
        byte[] header = new byte[32];
        int headerLength = (int)Math.Min(header.Length, sliceLength);
        if (headerLength < 28 || !file.TryRead(baseOffset, header.AsSpan(0, headerLength)))
        {
            throw new ExecutableAnalysisException("The file is too short for a Mach-O header.");
        }

        uint raw = BoundedFileReader.U32(header, 0, false);
        bool is64 = raw is 0xFEEDFACF or 0xCFFAEDFE;
        bool be = raw is 0xCEFAEDFE or 0xCFFAEDFE;
        if (raw is not (0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE))
        {
            throw new ExecutableAnalysisException("The file does not start with a Mach-O magic number.");
        }

        int headerSize = is64 ? 32 : 28;
        if (headerLength < headerSize)
        {
            throw new ExecutableAnalysisException("The file is too short for a Mach-O header.");
        }

        uint cpu = BoundedFileReader.U32(header, 4, be);
        uint fileType = BoundedFileReader.U32(header, 12, be);
        uint commandCount = BoundedFileReader.U32(header, 16, be);
        uint commandBytes = BoundedFileReader.U32(header, 20, be);
        uint flags = BoundedFileReader.U32(header, 24, be);

        List<ExecutableHint> hints = [];
        if (commandCount > MaxLoadCommands)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.ListCut, "load commands"));
            commandCount = MaxLoadCommands;
        }

        byte[] commands = [];
        if (commandBytes > 0)
        {
            byte[]? block = commandBytes <= sliceLength - headerSize ? file.ReadBytes(baseOffset + headerSize, commandBytes, MaxLoadCommandBytes) : null;
            if (block is null)
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "load commands"));
            }
            else
            {
                commands = block;
            }
        }

        List<ExecutableSection> sections = [];
        List<ExecutableImport> libraries = [];
        string subsystem = string.Empty;
        string entry = string.Empty;
        bool signed = false;
        uint symOffset = 0;
        uint symCount = 0;
        uint strOffset = 0;
        uint strSize = 0;

        int position = 0;
        for (uint c = 0; c < commandCount && position + 8 <= commands.Length; c++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint cmd = BoundedFileReader.U32(commands, position, be);
            uint size = BoundedFileReader.U32(commands, position + 4, be);
            if (size < 8 || size > commands.Length - position)
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "load command"));
                break;
            }

            ReadOnlySpan<byte> body = commands.AsSpan(position, (int)size);
            switch (cmd)
            {
                case 0x1 or 0x19:
                    ReadSegment(file, baseOffset, sliceLength, body, is64, be, sections, hints, cancellationToken);
                    break;
                case 0xC or 0x80000018 or 0x8000001F or 0x80000023:
                    if (libraries.Count < MaxLibraries && size >= 24 && BoundedFileReader.U32(body, 8, be) is var nameAt && nameAt < size
                        && StringIn(body, (int)nameAt) is { Length: > 0 } library)
                    {
                        libraries.Add(new ExecutableImport(library, -1));
                    }

                    break;
                case 0x1D:
                    signed = true;
                    break;
                case 0x2 when size >= 24:
                    symOffset = BoundedFileReader.U32(body, 8, be);
                    symCount = BoundedFileReader.U32(body, 12, be);
                    strOffset = BoundedFileReader.U32(body, 16, be);
                    strSize = BoundedFileReader.U32(body, 20, be);
                    break;
                case 0x80000028 when size >= 16:
                    entry = $"file offset 0x{BoundedFileReader.U64(body, 8, be):X}";
                    break;
                case 0x32 when size >= 16:
                    subsystem = $"{PlatformName(BoundedFileReader.U32(body, 8, be))} {Version(BoundedFileReader.U32(body, 12, be))}+";
                    break;
                case 0x24 or 0x25 or 0x2F or 0x30 when size >= 12 && subsystem.Length == 0:
                    subsystem = $"{(cmd == 0x24 ? "macOS" : cmd == 0x25 ? "iOS" : cmd == 0x2F ? "tvOS" : "watchOS")} {Version(BoundedFileReader.U32(body, 8, be))}+";
                    break;
            }

            position += (int)size;
        }

        (int exported, int imported, List<string> exportNames, bool? canary) =
            ReadSymbols(file, baseOffset, sliceLength, is64, be, symOffset, symCount, strOffset, strSize, hints, cancellationToken);

        bool pie = (flags & 0x200000) != 0;
        ExecutableKind kind = fileType switch
        {
            1 => ExecutableKind.Object,
            2 => pie ? ExecutableKind.PieExecutable : ExecutableKind.Executable,
            4 => ExecutableKind.Core,
            6 => ExecutableKind.SharedLibrary,
            8 => ExecutableKind.Bundle,
            11 => ExecutableKind.Driver,
            _ => ExecutableKind.Other,
        };

        List<ExecutableFlag> hardening =
        [
            new("PIE", fileType == 2 ? pie : null),
            new("NX heap", (flags & 0x1000000) != 0 ? true : is64 ? null : false),
            new("NX stack", (flags & 0x20000) == 0),
            new("Stack canary", canary),
        ];

        ExecutableHeuristics.AddSectionHints(sections, hints);
        if (file.Contains(baseOffset, Math.Min(4096, sliceLength), "UPX!"))
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.PackerSignature, "UPX"));
        }

        string thin = $"Mach-O {(is64 ? "64" : "32")}-bit {(be ? "big" : "little")}-endian";
        return new ExecutableReport
        {
            Format = ExecutableFormat.MachO,
            FormatName = universal is null ? thin : $"Mach-O universal ({universal.Count} slices), first readable: {thin}",
            Architecture = universal is null ? CpuName(cpu) : string.Join(", ", universal),
            Kind = kind,
            Subsystem = subsystem,
            EntryPoint = entry,
            Hardening = hardening,
            Imports = libraries,
            ImportedSymbols = symCount > 0 ? imported : -1,
            ExportedSymbols = exported,
            Exports = exportNames,
            Sections = sections,
            Signed = signed,
            Hints = hints,
            FileSize = file.Length,
        };
    }

    private static void ReadSegment(
        BoundedFileReader file, long baseOffset, long sliceLength, ReadOnlySpan<byte> body, bool is64, bool be,
        List<ExecutableSection> sections, List<ExecutableHint> hints, CancellationToken cancellationToken)
    {
        int segmentHeader = is64 ? 72 : 56;
        int sectionSize = is64 ? 80 : 68;
        if (body.Length < segmentHeader)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "segment"));
            return;
        }

        string segment = FixedName(body.Slice(8, 16));
        uint initialProtection = BoundedFileReader.U32(body, is64 ? 60 : 44, be);
        uint count = BoundedFileReader.U32(body, is64 ? 64 : 48, be);
        bool executable = (initialProtection & 4) != 0;
        bool writable = (initialProtection & 2) != 0;
        if (count > (uint)((body.Length - segmentHeader) / sectionSize))
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "segment sections"));
            count = (uint)((body.Length - segmentHeader) / sectionSize);
        }

        for (int i = 0; i < count; i++)
        {
            if (sections.Count >= MaxSections)
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.ListCut, "sections"));
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> s = body.Slice(segmentHeader + i * sectionSize, sectionSize);
            string name = FixedName(s[..16]);
            ulong size = is64 ? BoundedFileReader.U64(s, 40, be) : BoundedFileReader.U32(s, 36, be);
            uint offset = BoundedFileReader.U32(s, is64 ? 48 : 40, be);
            uint type = BoundedFileReader.U32(s, is64 ? 64 : 56, be) & 0xFF;
            bool zeroFill = type is 0x1 or 0xC or 0x12;
            bool inside = !zeroFill && offset != 0 && size > 0 && offset <= sliceLength && size <= long.MaxValue;
            long fileSize = inside ? Math.Min((long)size, sliceLength - offset) : 0;
            (double? entropy, bool sampled) = fileSize > 0 ? file.Entropy(baseOffset + offset, fileSize, cancellationToken) : (null, false);
            sections.Add(new ExecutableSection($"{segment},{name}", (long)Math.Min(size, long.MaxValue), fileSize, entropy, executable, writable, sampled));
        }
    }

    private static (int Exported, int Imported, List<string> Names, bool? Canary) ReadSymbols(
        BoundedFileReader file, long baseOffset, long sliceLength, bool is64, bool be, uint symOffset, uint symCount, uint strOffset, uint strSize,
        List<ExecutableHint> hints, CancellationToken cancellationToken)
    {
        List<string> names = [];
        if (symCount == 0 || symOffset >= sliceLength)
        {
            return (0, 0, names, null);
        }

        bool stringsOk = strSize > 0 && strOffset < sliceLength && strSize <= sliceLength - strOffset;
        bool canary = stringsOk && file.ContainsAny(baseOffset + strOffset, strSize, ["___stack_chk_fail"]);
        int entry = is64 ? 16 : 12;
        long total = symCount;
        if (total > MaxSymbols)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.ListCut, "symbols"));
            total = MaxSymbols;
        }

        total = Math.Min(total, (sliceLength - symOffset) / entry);
        int exported = 0;
        int imported = 0;
        const int Block = 4096;
        byte[] buffer = new byte[Block * 16];
        for (long first = 0; first < total; first += Block)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = (int)Math.Min(Block, total - first);
            Span<byte> chunk = buffer.AsSpan(0, count * entry);
            if (!file.TryRead(baseOffset + symOffset + first * entry, chunk))
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "symbol table"));
                break;
            }

            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> sym = chunk.Slice(i * entry, entry);
                byte type = sym[4];
                if ((type & 0xE0) != 0 || (type & 1) == 0)
                {
                    continue;
                }

                int kind = type & 0x0E;
                if (kind == 0)
                {
                    imported++;
                }
                else if (kind == 0x0E)
                {
                    exported++;
                    uint nameIndex = BoundedFileReader.U32(sym, 0, be);
                    if (names.Count < MaxExportNames && stringsOk && nameIndex < strSize
                        && file.ReadCString(baseOffset + strOffset + nameIndex) is { Length: > 0 } name)
                    {
                        names.Add(name);
                    }
                }
            }
        }

        return (exported, imported, names, canary);
    }

    private static string FixedName(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return BoundedFileReader.Printable(Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]));
    }

    private static string StringIn(ReadOnlySpan<byte> body, int offset)
    {
        ReadOnlySpan<byte> rest = body[offset..];
        int end = rest.IndexOf((byte)0);
        return BoundedFileReader.Printable(Encoding.UTF8.GetString(end < 0 ? rest : rest[..end]));
    }

    private static string Version(uint packed) => $"{packed >> 16}.{(packed >> 8) & 0xFF}";

    private static string PlatformName(uint platform) => platform switch
    {
        1 => "macOS",
        2 => "iOS",
        3 => "tvOS",
        4 => "watchOS",
        5 => "bridgeOS",
        6 => "Mac Catalyst",
        7 => "iOS simulator",
        8 => "tvOS simulator",
        9 => "watchOS simulator",
        11 => "visionOS",
        _ => $"platform {platform}",
    };

    private static string CpuName(uint cpu) => cpu switch
    {
        7 => "x86",
        0x01000007 => "x86-64",
        12 => "ARM",
        0x0100000C => "ARM64",
        0x0200000C => "ARM64_32",
        18 => "PowerPC",
        0x01000012 => "PowerPC64",
        _ => $"cpu 0x{cpu:X}",
    };
}
