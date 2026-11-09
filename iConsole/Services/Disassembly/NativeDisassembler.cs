using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using Iced.Intel;

namespace iConsole.Services.Disassembly;

/// <summary>
/// Disassembles the code sections of a native x86 or x86-64 executable (Windows PE or Linux ELF) with Iced. The sections
/// are swept linearly from their first byte, so data that sits among the code decodes as nonsense, as it does in any
/// linear-sweep disassembler. The file is only read as bytes; nothing in it is loaded or run.
/// </summary>
internal static class NativeDisassembler
{
    private const int MaxBytesShownPerInstruction = 8;
    private const uint ElfProgBits = 1;
    private const ulong ElfExecInstr = 4;

    public static void Disassemble(byte[] bytes, DisassemblyBudget budget)
    {
        budget.Add("── Disassembly (native) ────────────────────────────────────────");
        int nativeBytesLeft = DisassemblyBudget.MaxNativeBytes;
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 'M' && bytes[1] == 'Z')
            {
                DisassemblePe(bytes, budget, ref nativeBytesLeft);
            }
            else if (bytes.Length >= 4 && bytes[0] == 0x7F && bytes[1] == 'E' && bytes[2] == 'L' && bytes[3] == 'F')
            {
                DisassembleElf(bytes, budget, ref nativeBytesLeft);
            }
            else
            {
                budget.Add("  Not a Windows PE or Linux ELF file; there is nothing to disassemble.");
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or IOException or OverflowException)
        {
            budget.Lines.Add($"  The file's headers could not be read: {ex.Message}");
        }

        budget.FinishTruncation();
    }

    private static void DisassemblePe(byte[] bytes, DisassemblyBudget budget, ref int nativeBytesLeft)
    {
        PEHeaders headers = new(new MemoryStream(bytes, writable: false));
        Machine machine = headers.CoffHeader.Machine;
        int bitness = machine switch
        {
            Machine.I386 => 32,
            Machine.Amd64 => 64,
            _ => 0,
        };

        budget.Add($"  PE image, machine {machine}");
        if (bitness == 0)
        {
            budget.Add("  Only x86 and x86-64 code can be disassembled; this machine type is not supported.");
            return;
        }

        ulong imageBase = headers.PEHeader?.ImageBase ?? 0;
        foreach (SectionHeader section in headers.SectionHeaders)
        {
            if (budget.Exhausted)
            {
                return;
            }

            bool isCode = (section.SectionCharacteristics & (SectionCharacteristics.ContainsCode | SectionCharacteristics.MemExecute)) != 0;
            if (!isCode)
            {
                continue;
            }

            int size = section.VirtualSize > 0 ? Math.Min(section.SizeOfRawData, section.VirtualSize) : section.SizeOfRawData;
            if (section.PointerToRawData < 0 || size <= 0 || !InRange(bytes, (ulong)section.PointerToRawData, (ulong)size))
            {
                budget.Add($"  ; section {section.Name}: its data lies outside the file");
                continue;
            }

            ulong address = imageBase + (ulong)section.VirtualAddress;
            DecodeRegion(bytes, section.PointerToRawData, size, address, bitness, section.Name, budget, ref nativeBytesLeft);
        }
    }

    private static void DisassembleElf(byte[] bytes, DisassemblyBudget budget, ref int nativeBytesLeft)
    {
        if (bytes.Length < 52)
        {
            budget.Add("  The ELF header is cut short.");
            return;
        }

        byte elfClass = bytes[4];
        if (elfClass is not (1 or 2))
        {
            budget.Add("  Unknown ELF class.");
            return;
        }

        if (bytes[5] != 1)
        {
            budget.Add("  Big-endian ELF files are not supported.");
            return;
        }

        bool is64 = elfClass == 2;
        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x12));
        budget.Add($"  ELF {(is64 ? 64 : 32)}-bit, machine {ElfMachineName(machine)}");
        int bitness = machine switch
        {
            3 => 32,
            62 => 64,
            _ => 0,
        };

        if (bitness == 0)
        {
            budget.Add("  Only x86 and x86-64 code can be disassembled; this machine type is not supported.");
            return;
        }

        if (is64 && bytes.Length < 64)
        {
            budget.Add("  The ELF header is cut short.");
            return;
        }

        ulong sectionTable = is64
            ? BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(0x28))
            : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x20));
        int entrySize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(is64 ? 0x3A : 0x2E));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(is64 ? 0x3C : 0x30));
        int nameTableIndex = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(is64 ? 0x3E : 0x32));
        int minimumEntrySize = is64 ? 64 : 40;
        if (sectionTable == 0 || count == 0)
        {
            budget.Add("  The file has no section headers (it may be stripped), so its code cannot be located.");
            return;
        }

        if (entrySize < minimumEntrySize)
        {
            budget.Add("  The section headers are malformed.");
            return;
        }

        ElfSection? nameTable = nameTableIndex < count ? ReadElfSection(bytes, sectionTable, entrySize, nameTableIndex, is64) : null;
        for (int i = 0; i < count; i++)
        {
            if (budget.Exhausted)
            {
                return;
            }

            ElfSection? section = ReadElfSection(bytes, sectionTable, entrySize, i, is64);
            if (section is null)
            {
                budget.Add("  The section headers run past the end of the file.");
                return;
            }

            if (section.Type != ElfProgBits || (section.Flags & ElfExecInstr) == 0 || section.Size == 0)
            {
                continue;
            }

            string name = ElfSectionName(bytes, nameTable, section.NameOffset);
            if (!InRange(bytes, section.Offset, section.Size))
            {
                budget.Add($"  ; section {name}: its data lies outside the file");
                continue;
            }

            int size = (int)Math.Min(section.Size, int.MaxValue);
            DecodeRegion(bytes, (int)section.Offset, size, section.Address, bitness, name, budget, ref nativeBytesLeft);
        }
    }

    private static void DecodeRegion(
        byte[] bytes, int offset, int size, ulong address, int bitness, string name, DisassemblyBudget budget, ref int nativeBytesLeft)
    {
        if (nativeBytesLeft <= 0)
        {
            budget.Truncate($"the {DisassemblyBudget.MaxNativeBytes / (1024 * 1024)} MB limit on decoded code was reached");
            return;
        }

        int take = Math.Min(size, nativeBytesLeft);
        nativeBytesLeft -= take;
        budget.Add(string.Empty);
        budget.Add($"  ; section {name}  (address 0x{address:x}, {size:N0} bytes)");

        ByteArrayCodeReader reader = new(bytes, offset, take);
        Iced.Intel.Decoder decoder = Iced.Intel.Decoder.Create(bitness, reader, address, DecoderOptions.None);
        NasmFormatter formatter = new();
        StringOutput output = new();
        string addressFormat = bitness == 64 ? "x16" : "x8";
        while (reader.CanReadByte)
        {
            decoder.Decode(out Instruction instruction);
            formatter.Format(instruction, output);
            int start = offset + (int)(instruction.IP - address);
            int length = Math.Min(instruction.Length, bytes.Length - start);
            StringBuilder hex = new();
            for (int i = 0; i < length && i < MaxBytesShownPerInstruction; i++)
            {
                hex.Append(bytes[start + i].ToString("x2"));
            }

            string hexText = length > MaxBytesShownPerInstruction ? hex + "+" : hex.ToString();
            if (!budget.Add($"  {instruction.IP.ToString(addressFormat)}  {hexText,-17}{output.ToStringAndReset()}"))
            {
                return;
            }
        }

        // Said only now: a flag set earlier would make the budget refuse the part of the section that does fit.
        if (take < size)
        {
            budget.Truncate($"the {DisassemblyBudget.MaxNativeBytes / (1024 * 1024)} MB limit on decoded code was reached");
        }
    }

    private static bool InRange(byte[] bytes, ulong offset, ulong size) =>
        offset <= (ulong)bytes.Length && size <= (ulong)bytes.Length - offset;

    private static ElfSection? ReadElfSection(byte[] bytes, ulong table, int entrySize, int index, bool is64)
    {
        ulong entryOffset = table + ((ulong)index * (ulong)entrySize);
        if (table > (ulong)bytes.Length || !InRange(bytes, entryOffset, (ulong)entrySize))
        {
            return null;
        }

        ReadOnlySpan<byte> entry = bytes.AsSpan((int)entryOffset, entrySize);
        return is64
            ? new ElfSection(
                BinaryPrimitives.ReadUInt32LittleEndian(entry),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]),
                BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]),
                BinaryPrimitives.ReadUInt64LittleEndian(entry[0x10..]),
                BinaryPrimitives.ReadUInt64LittleEndian(entry[0x18..]),
                BinaryPrimitives.ReadUInt64LittleEndian(entry[0x20..]))
            : new ElfSection(
                BinaryPrimitives.ReadUInt32LittleEndian(entry),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[0xC..]),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[0x10..]),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[0x14..]));
    }

    private static string ElfSectionName(byte[] bytes, ElfSection? nameTable, uint nameOffset)
    {
        if (nameTable is null || nameOffset >= nameTable.Size || !InRange(bytes, nameTable.Offset, nameTable.Size))
        {
            return "?";
        }

        int start = (int)(nameTable.Offset + nameOffset);
        int end = (int)(nameTable.Offset + nameTable.Size);
        int length = 0;
        while (start + length < end && bytes[start + length] != 0 && length < 64)
        {
            length++;
        }

        return Encoding.ASCII.GetString(bytes, start, length);
    }

    private static string ElfMachineName(ushort machine) => machine switch
    {
        3 => "x86",
        62 => "x86-64",
        40 => "ARM",
        183 => "AArch64",
        243 => "RISC-V",
        _ => $"0x{machine:x}",
    };

    private sealed record ElfSection(uint NameOffset, uint Type, ulong Flags, ulong Address, ulong Offset, ulong Size);
}
