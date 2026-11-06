using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace iTerminal.Core.Test.Services.Executables;

/// <summary>Builds small PE, ELF and Mach-O files byte by byte, so the tests need no binary fixtures and can switch each flag.</summary>
internal static class ExecutableFixtures
{
    /// <summary>Bytes with an entropy near 8: a seeded generator, so a run is repeatable.</summary>
    public static byte[] RandomBytes(int length, int seed = 1)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    // ---- ELF ----------------------------------------------------------------------------------------------------------------------

    public sealed record ElfOptions
    {
        public bool Pie { get; init; } = true;
        public bool NonExecutableStack { get; init; } = true;
        public bool Relro { get; init; } = true;
        public bool BindNow { get; init; } = true;
        public bool Canary { get; init; } = true;
        public bool ProgramHeaders { get; init; } = true;
        public bool SectionHeaders { get; init; } = true;
        public bool Interpreter { get; init; } = true;
        public byte[]? Code { get; init; }
        public string TextSectionName { get; init; } = ".text";
    }

    /// <summary>A 64-bit little-endian x86-64 ELF with a dynamic section, one imported and one exported symbol.</summary>
    public static byte[] Elf64(ElfOptions? options = null)
    {
        options ??= new ElfOptions();
        byte[] code = options.Code ?? Encoding.ASCII.GetBytes(new string('A', 64));
        int programHeaderCount = options.ProgramHeaders ? 3 + (options.Relro ? 1 : 0) + (options.Interpreter ? 1 : 0) + 1 : 0;

        // Layout: ehdr, phdrs, code, dynstr, dynsym, dynamic, shstrtab, shdrs.
        int phOffset = 64;
        int codeOffset = phOffset + programHeaderCount * 56;
        byte[] dynstr = Encoding.ASCII.GetBytes("\0libc.so.6\0" + (options.Canary ? "__stack_chk_fail" : "puts") + "\0main\0");
        int dynstrOffset = codeOffset + code.Length;
        int dynsymOffset = (dynstrOffset + dynstr.Length + 7) & ~7;
        int dynsymSize = 3 * 24;
        int dynamicOffset = dynsymOffset + dynsymSize;
        List<(long Tag, long Value)> dynamic = [(1, 1), (5, dynstrOffset)];
        if (options.BindNow)
        {
            dynamic.Add((24, 0));
        }

        if (options.Pie)
        {
            dynamic.Add((0x6FFFFFFB, 0x08000000));
        }

        dynamic.Add((0, 0));
        int dynamicSize = dynamic.Count * 16;
        string shNames = "\0" + options.TextSectionName + "\0.dynstr\0.dynsym\0.dynamic\0.shstrtab\0";
        byte[] shstrtab = Encoding.ASCII.GetBytes(shNames);
        int shstrtabOffset = dynamicOffset + dynamicSize;
        int shOffset = (shstrtabOffset + shstrtab.Length + 7) & ~7;
        int sectionCount = options.SectionHeaders ? 6 : 0;
        int total = shOffset + sectionCount * 64;

        byte[] file = new byte[total];
        file[0] = 0x7F;
        file[1] = (byte)'E';
        file[2] = (byte)'L';
        file[3] = (byte)'F';
        file[4] = 2; // 64-bit
        file[5] = 1; // little-endian
        file[6] = 1;
        file[7] = 3; // Linux
        Put16(file, 16, options.Pie ? (ushort)3 : (ushort)2);
        Put16(file, 18, 62);
        Put32(file, 20, 1);
        Put64(file, 24, 0x1040);
        Put64(file, 32, options.ProgramHeaders ? (ulong)phOffset : 0);
        Put64(file, 40, options.SectionHeaders ? (ulong)shOffset : 0);
        Put16(file, 52, 64);
        Put16(file, 54, 56);
        Put16(file, 56, (ushort)programHeaderCount);
        Put16(file, 58, 64);
        Put16(file, 60, (ushort)sectionCount);
        Put16(file, 62, 5);

        int ph = phOffset;
        if (options.ProgramHeaders)
        {
            if (options.Interpreter)
            {
                ProgramHeader(file, ph, 3, 4, 0, 0, 0);
                ph += 56;
            }

            // One load segment that holds the whole file at virtual address == file offset.
            ProgramHeader(file, ph, 1, 5, 0, 0, (ulong)total);
            ph += 56;
            ProgramHeader(file, ph, 2, 6, (ulong)dynamicOffset, (ulong)dynamicOffset, (ulong)dynamicSize);
            ph += 56;
            ProgramHeader(file, ph, 0x6474E551, options.NonExecutableStack ? 6u : 7u, 0, 0, 0);
            ph += 56;
            if (options.Relro)
            {
                ProgramHeader(file, ph, 0x6474E552, 4, 0, 0, 0);
            }
        }

        code.CopyTo(file, codeOffset);
        dynstr.CopyTo(file, dynstrOffset);

        // Symbols: null, an undefined one named by offset 11, and a defined global.
        int symbol = dynsymOffset + 24;
        Put32(file, symbol, 11);
        file[symbol + 4] = 0x12; // global function
        Put16(file, symbol + 6, 0);
        symbol += 24;
        Put32(file, symbol, (uint)(11 + (options.Canary ? "__stack_chk_fail" : "puts").Length + 1));
        file[symbol + 4] = 0x12;
        Put16(file, symbol + 6, 1);

        int entry = dynamicOffset;
        foreach ((long tag, long value) in dynamic)
        {
            Put64(file, entry, (ulong)tag);
            Put64(file, entry + 8, (ulong)value);
            entry += 16;
        }

        shstrtab.CopyTo(file, shstrtabOffset);
        if (options.SectionHeaders)
        {
            int section = shOffset + 64;
            SectionHeader(file, section, NameOffset(shNames, options.TextSectionName), 1, 6, (ulong)codeOffset, (ulong)code.Length, 0, 0);
            section += 64;
            SectionHeader(file, section, NameOffset(shNames, ".dynstr"), 3, 2, (ulong)dynstrOffset, (ulong)dynstr.Length, 0, 0);
            section += 64;
            SectionHeader(file, section, NameOffset(shNames, ".dynsym"), 11, 2, (ulong)dynsymOffset, (ulong)dynsymSize, 2, 24);
            section += 64;
            SectionHeader(file, section, NameOffset(shNames, ".dynamic"), 6, 3, (ulong)dynamicOffset, (ulong)dynamicSize, 2, 16);
            section += 64;
            SectionHeader(file, section, NameOffset(shNames, ".shstrtab"), 3, 0, (ulong)shstrtabOffset, (ulong)shstrtab.Length, 0, 0);
        }

        return file;
    }

    /// <summary>A 32-bit big-endian PowerPC executable: just the header, so only the endian handling is exercised.</summary>
    public static byte[] Elf32BigEndianHeaderOnly()
    {
        byte[] file = new byte[52];
        file[0] = 0x7F;
        file[1] = (byte)'E';
        file[2] = (byte)'L';
        file[3] = (byte)'F';
        file[4] = 1;
        file[5] = 2;
        file[6] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(18), 20);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(24), 0x10000400);
        return file;
    }

    private static int NameOffset(string names, string name) => names.IndexOf("\0" + name + "\0", StringComparison.Ordinal) + 1;

    private static void ProgramHeader(byte[] file, int at, uint type, uint flags, ulong offset, ulong address, ulong size)
    {
        Put32(file, at, type);
        Put32(file, at + 4, flags);
        Put64(file, at + 8, offset);
        Put64(file, at + 16, address);
        Put64(file, at + 32, size);
        Put64(file, at + 40, size);
    }

    private static void SectionHeader(byte[] file, int at, int name, uint type, ulong flags, ulong offset, ulong size, uint link, ulong entrySize)
    {
        Put32(file, at, (uint)name);
        Put32(file, at + 4, type);
        Put64(file, at + 8, flags);
        Put64(file, at + 24, offset);
        Put64(file, at + 32, size);
        Put32(file, at + 40, link);
        Put64(file, at + 56, entrySize);
    }

    // ---- PE -----------------------------------------------------------------------------------------------------------------------

    public sealed record PeOptions
    {
        public bool Is64 { get; init; } = true;
        public ushort DllCharacteristics { get; init; } = 0x4160;
        public ushort Characteristics { get; init; } = 0x0022;
        public ushort Subsystem { get; init; } = 3;
        public uint TimeDateStamp { get; init; } = 1_700_000_000;
        public bool SecurityCookie { get; init; } = true;
        public bool SafeSehTable { get; init; }
        public bool Exports { get; init; } = true;
        public byte[]? Certificate { get; init; }
        public byte[]? Code { get; init; }
        public string TextName { get; init; } = ".text";
        public uint TextFlags { get; init; } = 0x60000020;
    }

    public static byte[] Pe(PeOptions? options = null)
    {
        options ??= new PeOptions();
        bool is64 = options.Is64;
        int optionalSize = is64 ? 240 : 224;
        int certificateOffset = 0x800;
        int total = 0x800 + (options.Certificate is null ? 0 : (8 + options.Certificate.Length + 7) & ~7);
        byte[] file = new byte[total];
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        Put32(file, 0x3C, 0x40);
        Put32(file, 0x40, 0x00004550);
        int coff = 0x44;
        Put16(file, coff, is64 ? (ushort)0x8664 : (ushort)0x14C);
        Put16(file, coff + 2, 2);
        Put32(file, coff + 4, options.TimeDateStamp);
        Put16(file, coff + 16, (ushort)optionalSize);
        Put16(file, coff + 18, options.Characteristics);

        int opt = coff + 20;
        Put16(file, opt, is64 ? (ushort)0x20B : (ushort)0x10B);
        Put32(file, opt + 4, 0x200);
        Put32(file, opt + 16, 0x1000); // entry point
        Put32(file, opt + 20, 0x1000);
        int afterBase = opt + 24;
        if (is64)
        {
            Put64(file, afterBase, 0x140000000);
            afterBase += 8;
        }
        else
        {
            Put32(file, afterBase, 0x2000);
            Put32(file, afterBase + 4, 0x400000);
            afterBase += 8;
        }

        Put32(file, afterBase, 0x1000); // section alignment
        Put32(file, afterBase + 4, 0x200); // file alignment
        Put16(file, afterBase + 8, 6);
        Put16(file, afterBase + 16, 6);
        Put32(file, afterBase + 24, 0x3000); // size of image
        Put32(file, afterBase + 28, 0x200); // size of headers
        Put16(file, afterBase + 36, options.Subsystem);
        Put16(file, afterBase + 38, options.DllCharacteristics);
        int rvaCountAt = is64 ? afterBase + 36 + 2 + 2 + 32 + 4 : afterBase + 36 + 2 + 2 + 16 + 4;
        Put32(file, rvaCountAt, 16);
        int directories = rvaCountAt + 4;

        // Sections: .text at RVA 0x1000 (file 0x200), .rdata at RVA 0x2000 (file 0x400).
        int sections = opt + optionalSize;
        Section(file, sections, options.TextName, 0x200, 0x1000, 0x200, 0x200, options.TextFlags);
        Section(file, sections + 40, ".rdata", 0x400, 0x2000, 0x400, 0x400, 0x40000040);
        byte[] text = options.Code ?? Encoding.ASCII.GetBytes(new string('C', 64));
        text.AsSpan(0, Math.Min(0x200, text.Length)).CopyTo(file.AsSpan(0x200));

        // Imports: one library, two functions.
        int rdata = 0x400;
        Put32(file, rdata, 0x2040);
        Put32(file, rdata + 12, 0x2080);
        Put32(file, rdata + 16, 0x2040);
        int thunk = rdata + 0x40;
        int thunkSize = is64 ? 8 : 4;
        Put32(file, thunk, 0x20A0);
        Put32(file, thunk + thunkSize, 0x20B0);
        Encoding.ASCII.GetBytes("kernel32.dll").CopyTo(file, rdata + 0x80);
        Put32(file, directories + 8, 0x2000);
        Put32(file, directories + 12, 40);

        if (options.Exports)
        {
            int export = rdata + 0x200;
            Put32(file, export + 20, 2);
            Put32(file, export + 24, 2);
            Put32(file, export + 28, 0x2250);
            Put32(file, export + 32, 0x2260);
            Put32(file, rdata + 0x260, 0x2280);
            Put32(file, rdata + 0x264, 0x2290);
            Encoding.ASCII.GetBytes("Alpha").CopyTo(file, rdata + 0x280);
            Encoding.ASCII.GetBytes("Beta").CopyTo(file, rdata + 0x290);
            Put32(file, directories, 0x2200);
            Put32(file, directories + 4, 0x60);
        }

        // Load configuration.
        int config = rdata + 0x300;
        if (is64)
        {
            Put32(file, config, 0x98);
            if (options.SecurityCookie)
            {
                Put64(file, config + 0x58, 0x140003000);
            }
        }
        else
        {
            Put32(file, config, 0x5C);
            if (options.SecurityCookie)
            {
                Put32(file, config + 0x3C, 0x403000);
            }

            if (options.SafeSehTable)
            {
                Put32(file, config + 0x40, 0x402100);
                Put32(file, config + 0x44, 3);
            }
        }

        Put32(file, directories + 10 * 8, 0x2300);
        Put32(file, directories + 10 * 8 + 4, is64 ? 0x98u : 0x5Cu);

        if (options.Certificate is { } certificate)
        {
            Put32(file, certificateOffset, (uint)(8 + certificate.Length));
            Put16(file, certificateOffset + 4, 0x200);
            Put16(file, certificateOffset + 6, 2);
            certificate.CopyTo(file, certificateOffset + 8);
            Put32(file, directories + 4 * 8, (uint)certificateOffset);
            Put32(file, directories + 4 * 8 + 4, (uint)(8 + certificate.Length));
        }

        return file;
    }

    private static void Section(byte[] file, int at, string name, int rawOffset, int rva, int rawSize, int virtualSize, uint characteristics)
    {
        Encoding.ASCII.GetBytes(name).AsSpan(0, Math.Min(8, name.Length)).CopyTo(file.AsSpan(at));
        Put32(file, at + 8, (uint)virtualSize);
        Put32(file, at + 12, (uint)rva);
        Put32(file, at + 16, (uint)rawSize);
        Put32(file, at + 20, (uint)rawOffset);
        Put32(file, at + 36, characteristics);
    }

    /// <summary>A PKCS #7 SignedData blob that holds one self-signed certificate: the shape of an Authenticode signature, without the signer.</summary>
    public static byte[] SignedDataWithCertificate(string subject, out string thumbprint)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2034, 1, 1, 0, 0, 0, TimeSpan.Zero));
        thumbprint = certificate.Thumbprint;

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            using (writer.PushSequence())
            {
                writer.WriteInteger(1);
                using (writer.PushSetOf())
                {
                }

                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier("1.2.840.113549.1.7.1");
                }

                using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    writer.WriteEncodedValue(certificate.RawData);
                }

                using (writer.PushSetOf())
                {
                }
            }
        }

        return writer.Encode();
    }

    // ---- Mach-O -------------------------------------------------------------------------------------------------------------------

    public sealed record MachOptions
    {
        public bool Pie { get; init; } = true;
        public bool Canary { get; init; } = true;
        public bool CodeSignature { get; init; } = true;
        public byte[]? Code { get; init; }
    }

    /// <summary>A 64-bit little-endian arm64 executable with one segment, one dylib, a symbol table and optionally a code signature.</summary>
    public static byte[] MachO64(MachOptions? options = null)
    {
        options ??= new MachOptions();
        byte[] code = options.Code ?? Encoding.ASCII.GetBytes(new string('M', 64));
        byte[] strings = Encoding.ASCII.GetBytes("\0" + (options.Canary ? "___stack_chk_fail" : "_puts") + "\0_main\0");
        string dylib = "/usr/lib/libSystem.B.dylib";
        int dylibCommand = (24 + dylib.Length + 1 + 7) & ~7;
        int segmentCommand = 72 + 80;
        int commandBytes = segmentCommand + dylibCommand + 24 + (options.CodeSignature ? 16 : 0);
        int commandCount = 3 + (options.CodeSignature ? 1 : 0);
        int codeOffset = 32 + commandBytes;
        int symbolOffset = (codeOffset + code.Length + 7) & ~7;
        int stringOffset = symbolOffset + 2 * 16;
        byte[] file = new byte[stringOffset + strings.Length];

        Put32(file, 0, 0xFEEDFACF);
        Put32(file, 4, 0x0100000C);
        Put32(file, 12, 2);
        Put32(file, 16, (uint)commandCount);
        Put32(file, 20, (uint)commandBytes);
        Put32(file, 24, options.Pie ? 0x200085u : 0x85u);

        int at = 32;
        Put32(file, at, 0x19);
        Put32(file, at + 4, (uint)segmentCommand);
        Encoding.ASCII.GetBytes("__TEXT").CopyTo(file, at + 8);
        Put64(file, at + 32, (ulong)code.Length);
        Put64(file, at + 40, (ulong)codeOffset);
        Put64(file, at + 48, (ulong)code.Length);
        Put32(file, at + 56, 7);
        Put32(file, at + 60, 5);
        Put32(file, at + 64, 1);
        int section = at + 72;
        Encoding.ASCII.GetBytes("__text").CopyTo(file, section);
        Encoding.ASCII.GetBytes("__TEXT").CopyTo(file, section + 16);
        Put64(file, section + 40, (ulong)code.Length);
        Put32(file, section + 48, (uint)codeOffset);
        at += segmentCommand;

        Put32(file, at, 0xC);
        Put32(file, at + 4, (uint)dylibCommand);
        Put32(file, at + 8, 24);
        Encoding.ASCII.GetBytes(dylib).CopyTo(file, at + 24);
        at += dylibCommand;

        Put32(file, at, 0x2);
        Put32(file, at + 4, 24);
        Put32(file, at + 8, (uint)symbolOffset);
        Put32(file, at + 12, 2);
        Put32(file, at + 16, (uint)stringOffset);
        Put32(file, at + 20, (uint)strings.Length);
        at += 24;

        if (options.CodeSignature)
        {
            Put32(file, at, 0x1D);
            Put32(file, at + 4, 16);
        }

        code.CopyTo(file, codeOffset);
        // An undefined external symbol and a defined external one.
        Put32(file, symbolOffset, 1);
        file[symbolOffset + 4] = 0x01;
        Put32(file, symbolOffset + 16, (uint)(1 + (options.Canary ? "___stack_chk_fail" : "_puts").Length + 1));
        file[symbolOffset + 16 + 4] = 0x0F;
        file[symbolOffset + 16 + 5] = 1;
        strings.CopyTo(file, stringOffset);
        return file;
    }

    /// <summary>A universal binary holding the given thin files.</summary>
    public static byte[] Fat(params byte[][] slices)
    {
        int offset = (8 + slices.Length * 20 + 0xFFF) & ~0xFFF;
        int total = offset + slices.Sum(s => (s.Length + 0xFFF) & ~0xFFF);
        byte[] file = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(0), 0xCAFEBABE);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)slices.Length);
        for (int i = 0; i < slices.Length; i++)
        {
            int entry = 8 + i * 20;
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(entry), i == 0 ? 0x01000007u : 0x0100000Cu);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(entry + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(entry + 12), (uint)slices[i].Length);
            slices[i].CopyTo(file, offset);
            offset += (slices[i].Length + 0xFFF) & ~0xFFF;
        }

        return file;
    }

    private static void Put16(byte[] file, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at), value);

    private static void Put32(byte[] file, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), value);

    private static void Put64(byte[] file, int at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at), value);
}
