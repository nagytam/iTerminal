using iConsole.Executables;

namespace iTerminal.Core.Services.Executables;

/// <summary>
/// Reads an ELF file by hand: the header, the program headers (stack, RELRO, dynamic section), the section table with entropy, and the
/// dynamic symbol table. Every count, offset and size comes from the file, so each is checked against the file and capped.
/// </summary>
internal static class ElfAnalyzer
{
    private const int MaxProgramHeaders = 256;
    private const int MaxSectionHeaders = 2048;
    private const int MaxDynamicEntries = 512;
    private const int MaxNeeded = 64;
    private const int MaxSymbols = 1_000_000;
    private const int MaxExportNames = 25;
    private const int MaxStringTable = 4 * 1024 * 1024;

    private const uint PtLoad = 1;
    private const uint PtDynamic = 2;
    private const uint PtInterp = 3;
    private const uint PtGnuStack = 0x6474E551;
    private const uint PtGnuRelro = 0x6474E552;

    private const ulong DtNeeded = 1;
    private const ulong DtStrtab = 5;
    private const ulong DtBindNow = 24;
    private const ulong DtFlags = 30;
    private const ulong DtFlags1 = 0x6FFFFFFB;
    private const ulong DfBindNow = 0x8;
    private const ulong Df1Now = 0x1;
    private const ulong Df1Pie = 0x08000000;

    private const uint ShtSymtab = 2;
    private const uint ShtDynsym = 11;
    private const uint ShtNobits = 8;

    private sealed record Segment(uint Type, uint Flags, ulong Offset, ulong VirtualAddress, ulong FileSize);

    private sealed record SectionEntry(string Name, uint Type, ulong Flags, ulong Offset, ulong Size, uint Link, ulong EntrySize);

    public static ExecutableReport Analyze(BoundedFileReader file, CancellationToken cancellationToken)
    {
        byte[] ident = new byte[64];
        int identLength = (int)Math.Min(ident.Length, file.Length);
        if (identLength < 16 || !file.TryRead(0, ident.AsSpan(0, identLength)))
        {
            throw new ExecutableAnalysisException("The file is too short for an ELF header.");
        }

        if (ident[4] is not (1 or 2) || ident[5] is not (1 or 2))
        {
            throw new ExecutableAnalysisException("The ELF class or byte order is not valid.");
        }

        bool is64 = ident[4] == 2;
        bool be = ident[5] == 2;
        int headerSize = is64 ? 64 : 52;
        if (identLength < headerSize)
        {
            throw new ExecutableAnalysisException("The file is too short for an ELF header.");
        }

        ushort type = BoundedFileReader.U16(ident, 16, be);
        ushort machine = BoundedFileReader.U16(ident, 18, be);
        ulong entry = is64 ? BoundedFileReader.U64(ident, 24, be) : BoundedFileReader.U32(ident, 24, be);
        ulong phoff = is64 ? BoundedFileReader.U64(ident, 32, be) : BoundedFileReader.U32(ident, 28, be);
        ulong shoff = is64 ? BoundedFileReader.U64(ident, 40, be) : BoundedFileReader.U32(ident, 32, be);
        int phentsize = BoundedFileReader.U16(ident, is64 ? 54 : 42, be);
        int phnum = BoundedFileReader.U16(ident, is64 ? 56 : 44, be);
        int shentsize = BoundedFileReader.U16(ident, is64 ? 58 : 46, be);
        int shnum = BoundedFileReader.U16(ident, is64 ? 60 : 48, be);
        int shstrndx = BoundedFileReader.U16(ident, is64 ? 62 : 50, be);

        List<ExecutableHint> hints = [];
        List<Segment> segments = ReadProgramHeaders(file, is64, be, phoff, phentsize, phnum, hints);
        bool hasProgramHeaders = segments.Count > 0;
        bool interpreter = segments.Any(s => s.Type == PtInterp);
        Segment? stack = segments.FirstOrDefault(s => s.Type == PtGnuStack);
        bool relro = segments.Any(s => s.Type == PtGnuRelro);

        (List<string> needed, bool bindNow, bool pieFlag) = ReadDynamic(file, is64, be, segments, hints, cancellationToken);

        List<SectionEntry> sectionTable = ReadSectionTable(file, is64, be, shoff, shentsize, shnum, shstrndx, hints);
        List<ExecutableSection> sections = BuildSections(file, sectionTable, segments, cancellationToken);
        (int exported, int imported, List<string> exportNames, bool? canary) = ReadSymbols(file, is64, be, sectionTable, hints, cancellationToken);

        bool pie = type == 3 && (pieFlag || interpreter);
        ExecutableKind kind = type switch
        {
            1 => ExecutableKind.Object,
            2 => ExecutableKind.Executable,
            3 => pie ? ExecutableKind.PieExecutable : ExecutableKind.SharedLibrary,
            4 => ExecutableKind.Core,
            _ => ExecutableKind.Other,
        };

        List<ExecutableFlag> flags =
        [
            new("PIE", pie ? true : type == 2 ? false : null),
            new("NX (non-executable stack)", hasProgramHeaders ? stack is not null && (stack.Flags & 1) == 0 : null),
            new("RELRO", hasProgramHeaders ? relro : null),
            new("Full RELRO (BIND_NOW)", hasProgramHeaders ? relro && bindNow : null),
            new("Stack canary", canary),
        ];

        ExecutableHeuristics.AddSectionHints(sections, hints);
        if (file.Contains(0, 4096, "UPX!"))
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.PackerSignature, "UPX"));
        }

        return new ExecutableReport
        {
            Format = ExecutableFormat.Elf,
            FormatName = $"ELF{(is64 ? 64 : 32)} {(be ? "big" : "little")}-endian",
            Architecture = MachineName(machine),
            Kind = kind,
            Subsystem = OsAbiName(ident[7]),
            EntryPoint = entry == 0 ? string.Empty : $"0x{entry:X}",
            Hardening = flags,
            Imports = [.. needed.Select(n => new ExecutableImport(n, -1))],
            ImportedSymbols = sectionTable.Any(s => s.Type == ShtDynsym) ? imported : -1,
            ExportedSymbols = exported,
            Exports = exportNames,
            Sections = sections,
            Hints = hints,
            FileSize = file.Length,
        };
    }

    private static List<Segment> ReadProgramHeaders(BoundedFileReader file, bool is64, bool be, ulong phoff, int phentsize, int phnum, List<ExecutableHint> hints)
    {
        List<Segment> segments = [];
        int minimum = is64 ? 56 : 32;
        if (phnum == 0 || phoff == 0)
        {
            return segments;
        }

        if (phentsize < minimum || phoff > long.MaxValue)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "program headers"));
            return segments;
        }

        if (phnum > MaxProgramHeaders)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.ListCut, "program headers"));
            phnum = MaxProgramHeaders;
        }

        byte[]? table = file.ReadBytes((long)phoff, (long)phnum * phentsize, MaxProgramHeaders * 1024);
        if (table is null)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "program headers"));
            return segments;
        }

        for (int i = 0; i < phnum; i++)
        {
            ReadOnlySpan<byte> h = table.AsSpan(i * phentsize, phentsize);
            uint type = BoundedFileReader.U32(h, 0, be);
            segments.Add(is64
                ? new Segment(type, BoundedFileReader.U32(h, 4, be), BoundedFileReader.U64(h, 8, be), BoundedFileReader.U64(h, 16, be), BoundedFileReader.U64(h, 32, be))
                : new Segment(type, BoundedFileReader.U32(h, 24, be), BoundedFileReader.U32(h, 4, be), BoundedFileReader.U32(h, 8, be), BoundedFileReader.U32(h, 16, be)));
        }

        return segments;
    }

    private static bool TryVirtualToOffset(List<Segment> segments, ulong address, out long offset)
    {
        offset = 0;
        foreach (Segment s in segments)
        {
            if (s.Type == PtLoad && address >= s.VirtualAddress && address - s.VirtualAddress < s.FileSize && s.Offset <= long.MaxValue / 2)
            {
                ulong result = s.Offset + (address - s.VirtualAddress);
                if (result <= long.MaxValue)
                {
                    offset = (long)result;
                    return true;
                }
            }
        }

        return false;
    }

    private static (List<string> Needed, bool BindNow, bool Pie) ReadDynamic(
        BoundedFileReader file, bool is64, bool be, List<Segment> segments, List<ExecutableHint> hints, CancellationToken cancellationToken)
    {
        List<string> needed = [];
        Segment? dynamic = segments.FirstOrDefault(s => s.Type == PtDynamic);
        if (dynamic is null || dynamic.Offset > long.MaxValue)
        {
            return (needed, false, false);
        }

        int entrySize = is64 ? 16 : 8;
        long count = (long)Math.Min(dynamic.FileSize / (ulong)entrySize, MaxDynamicEntries);
        byte[]? table = file.ReadBytes((long)dynamic.Offset, count * entrySize, MaxDynamicEntries * 16);
        if (table is null)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "dynamic section"));
            return (needed, false, false);
        }

        List<ulong> neededOffsets = [];
        ulong strtab = 0;
        bool bindNow = false;
        bool pie = false;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ulong tag = is64 ? BoundedFileReader.U64(table, i * 16, be) : BoundedFileReader.U32(table, i * 8, be);
            ulong value = is64 ? BoundedFileReader.U64(table, i * 16 + 8, be) : BoundedFileReader.U32(table, i * 8 + 4, be);
            if (tag == 0)
            {
                break;
            }

            switch (tag)
            {
                case DtNeeded:
                    if (neededOffsets.Count < MaxNeeded)
                    {
                        neededOffsets.Add(value);
                    }

                    break;
                case DtStrtab:
                    strtab = value;
                    break;
                case DtBindNow:
                    bindNow = true;
                    break;
                case DtFlags:
                    bindNow |= (value & DfBindNow) != 0;
                    break;
                case DtFlags1:
                    bindNow |= (value & Df1Now) != 0;
                    pie |= (value & Df1Pie) != 0;
                    break;
            }
        }

        if (neededOffsets.Count > 0 && TryVirtualToOffset(segments, strtab, out long strtabOffset))
        {
            foreach (ulong nameOffset in neededOffsets)
            {
                if (nameOffset < int.MaxValue && file.ReadCString(strtabOffset + (long)nameOffset) is { Length: > 0 } name)
                {
                    needed.Add(name);
                }
            }
        }

        return (needed, bindNow, pie);
    }

    private static List<SectionEntry> ReadSectionTable(
        BoundedFileReader file, bool is64, bool be, ulong shoff, int shentsize, int shnum, int shstrndx, List<ExecutableHint> hints)
    {
        List<SectionEntry> sections = [];
        int minimum = is64 ? 64 : 40;
        if (shnum == 0 || shoff == 0)
        {
            return sections;
        }

        if (shentsize < minimum || shoff > long.MaxValue)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "section headers"));
            return sections;
        }

        if (shnum > MaxSectionHeaders)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.ListCut, "section headers"));
            shnum = MaxSectionHeaders;
        }

        byte[]? table = file.ReadBytes((long)shoff, (long)shnum * shentsize, MaxSectionHeaders * 1024);
        if (table is null)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "section headers"));
            return sections;
        }

        // The names live in the section named by shstrndx.
        byte[]? names = null;
        if (shstrndx < shnum)
        {
            ReadOnlySpan<byte> s = table.AsSpan(shstrndx * shentsize, shentsize);
            ulong offset = is64 ? BoundedFileReader.U64(s, 24, be) : BoundedFileReader.U32(s, 16, be);
            ulong size = is64 ? BoundedFileReader.U64(s, 32, be) : BoundedFileReader.U32(s, 20, be);
            if (offset <= long.MaxValue && size <= MaxStringTable)
            {
                names = file.ReadBytes((long)offset, (long)size, MaxStringTable);
            }
        }

        for (int i = 0; i < shnum; i++)
        {
            ReadOnlySpan<byte> h = table.AsSpan(i * shentsize, shentsize);
            uint nameIndex = BoundedFileReader.U32(h, 0, be);
            uint type = BoundedFileReader.U32(h, 4, be);
            sections.Add(is64
                ? new SectionEntry(NameAt(names, nameIndex), type, BoundedFileReader.U64(h, 8, be), BoundedFileReader.U64(h, 24, be), BoundedFileReader.U64(h, 32, be), BoundedFileReader.U32(h, 40, be), BoundedFileReader.U64(h, 56, be))
                : new SectionEntry(NameAt(names, nameIndex), type, BoundedFileReader.U32(h, 8, be), BoundedFileReader.U32(h, 16, be), BoundedFileReader.U32(h, 20, be), BoundedFileReader.U32(h, 24, be), BoundedFileReader.U32(h, 36, be)));
        }

        return sections;
    }

    private static string NameAt(byte[]? table, uint index)
    {
        if (table is null || index >= table.Length)
        {
            return string.Empty;
        }

        int start = (int)index;
        int end = Array.IndexOf(table, (byte)0, start);
        if (end < 0)
        {
            end = table.Length;
        }

        return BoundedFileReader.Printable(System.Text.Encoding.UTF8.GetString(table, start, Math.Min(end - start, 256)));
    }

    private static List<ExecutableSection> BuildSections(
        BoundedFileReader file, List<SectionEntry> table, List<Segment> segments, CancellationToken cancellationToken)
    {
        List<ExecutableSection> sections = [];
        if (table.Count > 0)
        {
            foreach (SectionEntry s in table.Where(s => s.Type != 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool hasBytes = s.Type != ShtNobits && s.Size > 0 && s.Offset <= long.MaxValue && s.Size <= long.MaxValue;
                (double? entropy, bool sampled) = hasBytes ? file.Entropy((long)s.Offset, (long)s.Size, cancellationToken) : (null, false);
                long fileSize = hasBytes && file.InRange((long)s.Offset, 0) ? Math.Min((long)s.Size, file.Length - (long)s.Offset) : 0;
                sections.Add(new ExecutableSection(s.Name, (long)Math.Min(s.Size, long.MaxValue), fileSize, entropy, (s.Flags & 4) != 0, (s.Flags & 1) != 0, sampled));
            }

            return sections;
        }

        // A file without section headers (stripped of them, or a core) is described by its loadable segments instead.
        int index = 0;
        foreach (Segment s in segments.Where(s => s.Type == PtLoad))
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool ok = s.Offset <= long.MaxValue && s.FileSize <= long.MaxValue;
            (double? entropy, bool sampled) = ok ? file.Entropy((long)s.Offset, (long)s.FileSize, cancellationToken) : (null, false);
            long fileSize = ok && file.InRange((long)s.Offset, 0) ? Math.Min((long)s.FileSize, file.Length - (long)s.Offset) : 0;
            sections.Add(new ExecutableSection($"LOAD[{index++}]", ok ? (long)s.FileSize : 0, fileSize, entropy, (s.Flags & 1) != 0, (s.Flags & 2) != 0, sampled));
        }

        return sections;
    }

    private static (int Exported, int Imported, List<string> Names, bool? Canary) ReadSymbols(
        BoundedFileReader file, bool is64, bool be, List<SectionEntry> table, List<ExecutableHint> hints, CancellationToken cancellationToken)
    {
        List<string> names = [];
        int exported = 0;
        int imported = 0;
        bool sawSymbols = false;
        bool canary = false;
        int symbolSize = is64 ? 24 : 16;
        foreach (SectionEntry symtab in table.Where(s => s.Type is ShtSymtab or ShtDynsym))
        {
            if (symtab.Link >= table.Count || symtab.Offset > long.MaxValue || symtab.Size > long.MaxValue)
            {
                continue;
            }

            sawSymbols = true;
            SectionEntry strtab = table[(int)symtab.Link];
            bool stringsOk = strtab.Offset <= long.MaxValue && strtab.Size <= long.MaxValue && file.InRange((long)strtab.Offset, 0);
            if (stringsOk)
            {
                canary |= file.ContainsAny((long)strtab.Offset, (long)strtab.Size, ["__stack_chk_fail", "__stack_smash_handler"]);
            }

            if (symtab.Type != ShtDynsym)
            {
                continue;
            }

            long total = (long)Math.Min(symtab.Size / (ulong)symbolSize, (ulong)MaxSymbols);
            if ((long)(symtab.Size / (ulong)symbolSize) > MaxSymbols)
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.ListCut, "symbols"));
            }

            const int Block = 4096;
            byte[] buffer = new byte[Block * 24];
            for (long first = 0; first < total; first += Block)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(Block, total - first);
                Span<byte> chunk = buffer.AsSpan(0, count * symbolSize);
                if (!file.TryRead((long)symtab.Offset + first * symbolSize, chunk))
                {
                    hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "symbol table"));
                    break;
                }

                for (int i = 0; i < count; i++)
                {
                    ReadOnlySpan<byte> sym = chunk.Slice(i * symbolSize, symbolSize);
                    uint nameIndex = BoundedFileReader.U32(sym, 0, be);
                    byte info = is64 ? sym[4] : sym[12];
                    byte other = is64 ? sym[5] : sym[13];
                    ushort sectionIndex = BoundedFileReader.U16(sym, is64 ? 6 : 14, be);
                    if (sectionIndex == 0)
                    {
                        if (nameIndex != 0)
                        {
                            imported++;
                        }

                        continue;
                    }

                    int bind = info >> 4;
                    if (bind is 1 or 2 && (other & 3) == 0)
                    {
                        exported++;
                        if (names.Count < MaxExportNames && stringsOk && nameIndex < (ulong)strtab.Size
                            && file.ReadCString((long)strtab.Offset + nameIndex) is { Length: > 0 } name)
                        {
                            names.Add(name);
                        }
                    }
                }
            }
        }

        return (exported, imported, names, sawSymbols ? canary : null);
    }

    private static string MachineName(ushort machine) => machine switch
    {
        3 => "x86",
        62 => "x86-64",
        40 => "ARM",
        183 => "ARM64",
        8 => "MIPS",
        20 => "PowerPC",
        21 => "PowerPC64",
        22 => "S/390",
        243 => "RISC-V",
        42 => "SuperH",
        2 => "SPARC",
        43 => "SPARC V9",
        4 => "m68k",
        258 => "LoongArch",
        _ => $"machine {machine}",
    };

    private static string OsAbiName(byte abi) => abi switch
    {
        0 => string.Empty,
        2 => "NetBSD",
        3 => "Linux",
        6 => "Solaris",
        9 => "FreeBSD",
        12 => "OpenBSD",
        97 => "ARM",
        255 => "Standalone",
        _ => $"OS ABI {abi}",
    };
}
