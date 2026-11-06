using System.Reflection.PortableExecutable;
using iConsole.Executables;

namespace iTerminal.Core.Services.Executables;

/// <summary>
/// Reads a Windows PE file. The headers come from the BCL's <see cref="PEHeaders"/>; the import and export tables, the load
/// configuration and the certificate table are walked by hand, every offset checked against the file.
/// </summary>
internal static class PeAnalyzer
{
    private const int MaxSections = 512;
    private const int MaxImportDescriptors = 1024;
    private const int MaxThunksPerLibrary = 65_536;

    /// <summary>The most thunks all libraries together may read, so 1,024 descriptors cannot each read a big table.</summary>
    private const long MaxThunksTotal = 262_144;
    private const int MaxExportNames = 25;
    private const int MaxCertificateBlob = 1024 * 1024;

    private const ushort DllCharHighEntropyVa = 0x0020;
    private const ushort DllCharDynamicBase = 0x0040;
    private const ushort DllCharNxCompat = 0x0100;
    private const ushort DllCharNoSeh = 0x0400;
    private const ushort DllCharGuardCf = 0x4000;
    private const ushort CoffRelocsStripped = 0x0001;

    public static ExecutableReport Analyze(string path, BoundedFileReader file, CancellationToken cancellationToken)
    {
        PEHeaders headers;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            headers = new PEHeaders(stream);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or ArgumentException or OverflowException)
        {
            throw new ExecutableAnalysisException(ex.Message, ex);
        }

        if (headers.PEHeader is not { } pe)
        {
            throw new ExecutableAnalysisException("The file has no optional header, so it is not an image.");
        }

        List<ExecutableHint> hints = [];
        bool is64 = pe.Magic == PEMagic.PE32Plus;
        ushort machine = (ushort)headers.CoffHeader.Machine;
        bool managed = headers.CorHeader is not null;

        (IReadOnlyList<ExecutableImport> imports, int importedSymbols) = ReadImports(file, headers, pe, is64, hints, cancellationToken);
        (int exportCount, IReadOnlyList<string> exports) = ReadExports(file, headers, pe);
        IReadOnlyList<ExecutableSection> sections = ReadSections(file, headers, hints, cancellationToken);
        (bool signed, IReadOnlyList<ExecutableCertificate> certificates) = ReadSignature(file, pe);

        ushort dll = (ushort)pe.DllCharacteristics;
        LoadConfiguration loadConfig = ReadLoadConfiguration(file, headers, pe, is64);
        bool relocsStripped = ((ushort)headers.CoffHeader.Characteristics & CoffRelocsStripped) != 0;
        List<ExecutableFlag> flags =
        [
            new("ASLR", (dll & DllCharDynamicBase) != 0 && !relocsStripped),
            new("High-entropy ASLR", is64 ? (dll & DllCharHighEntropyVa) != 0 : null),
            new("DEP/NX", (dll & DllCharNxCompat) != 0),
            new("CFG", (dll & DllCharGuardCf) != 0),
            new("SafeSEH", SafeSeh(machine, is64, dll, loadConfig)),
            new("Stack cookie (/GS)", loadConfig.Present ? loadConfig.SecurityCookie != 0 : false),
        ];

        if (!managed && imports.Count > 0 && importedSymbols is >= 0 and < 5 && !headers.IsDll)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.FewImports));
        }

        ExecutableHeuristics.AddSectionHints(sections, hints);

        DateTime? timestamp = null;
        uint stamp = (uint)headers.CoffHeader.TimeDateStamp;
        if (stamp == 0 || stamp > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86_400)
        {
            hints.Add(new ExecutableHint(ExecutableHintKind.TimestampUnreliable));
        }
        else
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(stamp).UtcDateTime;
        }

        ExecutableKind kind = headers.IsDll
            ? ExecutableKind.SharedLibrary
            : pe.Subsystem == Subsystem.Native ? ExecutableKind.Driver : ExecutableKind.Executable;

        return new ExecutableReport
        {
            Format = ExecutableFormat.PE,
            FormatName = is64 ? "PE32+ (64-bit)" : "PE32 (32-bit)",
            Architecture = MachineName(machine),
            Kind = kind,
            Subsystem = SubsystemName(pe.Subsystem),
            Timestamp = timestamp,
            Managed = managed,
            EntryPoint = pe.AddressOfEntryPoint == 0 ? string.Empty : $"RVA 0x{pe.AddressOfEntryPoint:X}",
            Hardening = flags,
            Imports = imports,
            ImportedSymbols = importedSymbols,
            ExportedSymbols = exportCount,
            Exports = exports,
            Sections = sections,
            Signed = signed,
            Certificates = certificates,
            Hints = hints,
            FileSize = file.Length,
        };
    }

    private readonly record struct LoadConfiguration(bool Present, ulong SecurityCookie, ulong SeHandlerTable, ulong SeHandlerCount);

    /// <summary>SafeSEH only exists in 32-bit x86 images: null elsewhere. An image that declares no SEH at all is safe.</summary>
    private static bool? SafeSeh(ushort machine, bool is64, ushort dllCharacteristics, LoadConfiguration config)
    {
        if (is64 || machine != 0x14C)
        {
            return null;
        }

        if ((dllCharacteristics & DllCharNoSeh) != 0)
        {
            return true;
        }

        return config.Present && config.SeHandlerTable != 0 && config.SeHandlerCount != 0;
    }

    private static LoadConfiguration ReadLoadConfiguration(BoundedFileReader file, PEHeaders headers, PEHeader pe, bool is64)
    {
        DirectoryEntry directory = pe.LoadConfigTableDirectory;
        if (directory.Size < 4 || !TryRvaToOffset(headers, directory.RelativeVirtualAddress, out long offset))
        {
            return default;
        }

        int length = (int)Math.Min(directory.Size, 0x100);
        byte[]? block = file.ReadBytes(offset, Math.Min(length, file.Length - offset), 0x100);
        if (block is null || block.Length < 4)
        {
            return default;
        }

        // The structure's own size field says which of the later fields exist.
        uint declared = BoundedFileReader.U32(block, 0, false);
        int cookieAt = is64 ? 0x58 : 0x3C;
        int fieldSize = is64 ? 8 : 4;
        ulong cookie = 0;
        ulong seTable = 0;
        ulong seCount = 0;
        if (declared >= cookieAt + fieldSize && block.Length >= cookieAt + fieldSize)
        {
            cookie = is64 ? BoundedFileReader.U64(block, cookieAt, false) : BoundedFileReader.U32(block, cookieAt, false);
        }

        if (!is64 && declared >= 0x48 && block.Length >= 0x48)
        {
            seTable = BoundedFileReader.U32(block, 0x40, false);
            seCount = BoundedFileReader.U32(block, 0x44, false);
        }

        return new LoadConfiguration(true, cookie, seTable, seCount);
    }

    private static IReadOnlyList<ExecutableSection> ReadSections(BoundedFileReader file, PEHeaders headers, List<ExecutableHint> hints, CancellationToken cancellationToken)
    {
        List<ExecutableSection> sections = [];
        foreach (SectionHeader header in headers.SectionHeaders)
        {
            if (sections.Count >= MaxSections)
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.ListCut, "sections"));
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            long rawSize = Math.Max(0, header.SizeOfRawData);
            long rawOffset = Math.Max(0, header.PointerToRawData);
            (double? entropy, bool sampled) = rawSize > 0 ? file.Entropy(rawOffset, rawSize, cancellationToken) : (null, false);
            long fileSize = file.InRange(rawOffset, 0) ? Math.Min(rawSize, Math.Max(0, file.Length - rawOffset)) : 0;
            sections.Add(new ExecutableSection(
                BoundedFileReader.Printable(header.Name),
                header.VirtualSize > 0 ? header.VirtualSize : rawSize,
                fileSize,
                entropy,
                (header.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0,
                (header.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0,
                sampled));
        }

        return sections;
    }

    private static (IReadOnlyList<ExecutableImport> Imports, int Symbols) ReadImports(
        BoundedFileReader file, PEHeaders headers, PEHeader pe, bool is64, List<ExecutableHint> hints, CancellationToken cancellationToken)
    {
        DirectoryEntry directory = pe.ImportTableDirectory;
        if (directory.Size == 0 || !TryRvaToOffset(headers, directory.RelativeVirtualAddress, out long tableOffset))
        {
            return ([], 0);
        }

        List<ExecutableImport> imports = [];
        long symbols = 0;
        long thunkBudget = MaxThunksTotal;
        int thunkSize = is64 ? 8 : 4;
        byte[] descriptor = new byte[20];
        for (int i = 0; i < MaxImportDescriptors; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.TryRead(tableOffset + (long)i * 20, descriptor))
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.Malformed, "import table"));
                break;
            }

            uint originalFirstThunk = BoundedFileReader.U32(descriptor, 0, false);
            uint nameRva = BoundedFileReader.U32(descriptor, 12, false);
            uint firstThunk = BoundedFileReader.U32(descriptor, 16, false);
            if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0)
            {
                break;
            }

            string name = TryRvaToOffset(headers, (int)Math.Min(nameRva, int.MaxValue), out long nameOffset) ? file.ReadCString(nameOffset) ?? "?" : "?";
            int functions = 0;
            uint thunkRva = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
            if (thunkRva != 0 && TryRvaToOffset(headers, (int)Math.Min(thunkRva, int.MaxValue), out long thunkOffset))
            {
                long available = Math.Max(0, file.Length - thunkOffset) / thunkSize;
                int count = (int)Math.Min(Math.Min(MaxThunksPerLibrary, available), thunkBudget);
                thunkBudget -= count;
                byte[]? block = file.ReadBytes(thunkOffset, (long)count * thunkSize, MaxThunksPerLibrary * 8);
                if (block is not null)
                {
                    while (functions < count && block.AsSpan(functions * thunkSize, thunkSize).IndexOfAnyExcept((byte)0) >= 0)
                    {
                        functions++;
                    }

                    if (functions == MaxThunksPerLibrary)
                    {
                        hints.Add(new ExecutableHint(ExecutableHintKind.ListCut, name));
                    }
                }
            }

            symbols += functions;
            imports.Add(new ExecutableImport(name, functions));
            if (i == MaxImportDescriptors - 1)
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.ListCut, "imports"));
            }
        }

        return (imports, (int)Math.Min(symbols, int.MaxValue));
    }

    private static (int Count, IReadOnlyList<string> Names) ReadExports(BoundedFileReader file, PEHeaders headers, PEHeader pe)
    {
        DirectoryEntry directory = pe.ExportTableDirectory;
        if (directory.Size < 40 || !TryRvaToOffset(headers, directory.RelativeVirtualAddress, out long offset))
        {
            return (0, []);
        }

        byte[] table = new byte[40];
        if (!file.TryRead(offset, table))
        {
            return (0, []);
        }

        uint functions = BoundedFileReader.U32(table, 20, false);
        uint nameCount = BoundedFileReader.U32(table, 24, false);
        uint namesRva = BoundedFileReader.U32(table, 32, false);
        List<string> names = [];
        if (nameCount > 0 && TryRvaToOffset(headers, (int)Math.Min(namesRva, int.MaxValue), out long namesOffset))
        {
            for (int i = 0; i < Math.Min(nameCount, MaxExportNames); i++)
            {
                byte[] entry = new byte[4];
                if (!file.TryRead(namesOffset + i * 4L, entry))
                {
                    break;
                }

                uint rva = BoundedFileReader.U32(entry, 0, false);
                if (TryRvaToOffset(headers, (int)Math.Min(rva, int.MaxValue), out long nameOffset) && file.ReadCString(nameOffset) is { } name)
                {
                    names.Add(name);
                }
            }
        }

        return ((int)Math.Min(functions, 10_000_000), names);
    }

    private static (bool Signed, IReadOnlyList<ExecutableCertificate> Certificates) ReadSignature(BoundedFileReader file, PEHeader pe)
    {
        // The certificate table's "RVA" is a file offset.
        DirectoryEntry directory = pe.CertificateTableDirectory;
        if (directory.Size < 8 || directory.RelativeVirtualAddress <= 0)
        {
            return (false, []);
        }

        List<ExecutableCertificate> certificates = [];
        long position = directory.RelativeVirtualAddress;
        long end = position + directory.Size;
        byte[] header = new byte[8];
        for (int i = 0; i < 4 && position + 8 <= end; i++)
        {
            if (!file.TryRead(position, header))
            {
                break;
            }

            uint length = BoundedFileReader.U32(header, 0, false);
            ushort type = BoundedFileReader.U16(header, 6, false);
            if (length < 8 || position + length > end)
            {
                break;
            }

            // Type 2 is PKCS #7 SignedData, the only one Authenticode uses.
            if (type == 2 && file.ReadBytes(position + 8, Math.Min(length - 8, MaxCertificateBlob), MaxCertificateBlob) is { } blob)
            {
                certificates.AddRange(AuthenticodeCertificates.Parse(blob));
            }

            position += (length + 7L) & ~7L;
        }

        return (true, certificates);
    }

    /// <summary>Maps a relative virtual address to a file offset through the section table; false for an address no section holds.</summary>
    private static bool TryRvaToOffset(PEHeaders headers, int rva, out long offset)
    {
        offset = 0;
        if (rva <= 0)
        {
            return false;
        }

        if (rva < headers.PEHeader!.SizeOfHeaders)
        {
            offset = rva;
            return true;
        }

        foreach (SectionHeader section in headers.SectionHeaders)
        {
            long start = (uint)section.VirtualAddress;
            long size = Math.Max((uint)section.VirtualSize, (uint)section.SizeOfRawData);
            if (rva >= start && rva < start + size)
            {
                offset = (uint)section.PointerToRawData + (rva - start);
                return true;
            }
        }

        return false;
    }

    private static string MachineName(ushort machine) => machine switch
    {
        0x14C => "x86",
        0x8664 => "x86-64",
        0x1C0 => "ARM",
        0x1C4 => "ARM Thumb-2",
        0xAA64 => "ARM64",
        0xA641 => "ARM64EC",
        0x200 => "Itanium",
        0x1F0 => "PowerPC",
        0x5032 => "RISC-V 32",
        0x5064 => "RISC-V 64",
        0x6232 => "LoongArch 32",
        0x6264 => "LoongArch 64",
        0xEBC => "EFI byte code",
        _ => $"machine 0x{machine:X4}",
    };

    private static string SubsystemName(Subsystem subsystem) => subsystem switch
    {
        Subsystem.Native => "Native (driver)",
        Subsystem.WindowsGui => "Windows GUI",
        Subsystem.WindowsCui => "Windows console",
        Subsystem.PosixCui => "POSIX console",
        Subsystem.WindowsCEGui => "Windows CE",
        Subsystem.EfiApplication => "EFI application",
        Subsystem.EfiBootServiceDriver => "EFI boot service driver",
        Subsystem.EfiRuntimeDriver => "EFI runtime driver",
        Subsystem.EfiRom => "EFI ROM",
        Subsystem.Xbox => "Xbox",
        Subsystem.WindowsBootApplication => "Windows boot application",
        Subsystem.Unknown => string.Empty,
        _ => $"subsystem {(int)subsystem}",
    };
}
