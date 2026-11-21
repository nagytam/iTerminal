using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using iConsole.Services.Disassembly;

namespace iConsole.Test.Services.Disassembly;

[TestClass]
public class DisassemblyBuilderTest
{
    // push rbp; mov rbp,rsp; mov eax,0x2a; pop rbp; ret
    private static readonly byte[] SampleCode = [0x55, 0x48, 0x89, 0xE5, 0xB8, 0x2A, 0x00, 0x00, 0x00, 0x5D, 0xC3];

    // ── managed IL ──────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Build_ManagedAssembly_ListsMethodsWithResolvedNames()
    {
        IReadOnlyList<string> lines = DisassemblyBuilder.Build(BuildManagedAssembly(0), CancellationToken.None);

        string text = string.Join('\n', lines);
        StringAssert.Contains(text, "Disassembly (IL)");
        StringAssert.Contains(text, ".method Demo.Greeter::Hello");
        StringAssert.Contains(text, "ldstr \"hello world\"");
        StringAssert.Contains(text, "call System.Console::WriteLine");
        StringAssert.Contains(text, "ret");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_ManagedMethodOverInstructionLimit_IsCutAndSaysSo()
    {
        IReadOnlyList<string> lines = DisassemblyBuilder.Build(
            BuildManagedAssembly(DisassemblyBudget.MaxInstructionsPerMethod + 1000), CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("method truncated")), "A method past the instruction limit must say it was cut.");
        Assert.IsTrue(lines.Count < DisassemblyBudget.MaxInstructionsPerMethod + 100);
    }

    // ── native PE and ELF ───────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Build_NativePe_DecodesTheCodeSection()
    {
        IReadOnlyList<string> lines = DisassemblyBuilder.Build(BuildPe(0x8664, SampleCode), CancellationToken.None);

        string text = string.Join('\n', lines);
        StringAssert.Contains(text, "Disassembly (native)");
        StringAssert.Contains(text, "section .text");
        StringAssert.Contains(text, "0000000140001000");
        StringAssert.Contains(text, "push rbp");
        StringAssert.Contains(text, "mov rbp,rsp");
        StringAssert.Contains(text, "mov eax,2Ah");
        StringAssert.Contains(text, "ret");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_NativeElf_DecodesTheExecutableSection()
    {
        IReadOnlyList<string> lines = DisassemblyBuilder.Build(BuildElf(62, SampleCode), CancellationToken.None);

        string text = string.Join('\n', lines);
        StringAssert.Contains(text, "ELF 64-bit");
        StringAssert.Contains(text, "section .text");
        StringAssert.Contains(text, "push rbp");
        StringAssert.Contains(text, "ret");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_PeForArm64_SaysItIsNotSupported()
    {
        IReadOnlyList<string> lines = DisassemblyBuilder.Build(BuildPe(0xAA64, SampleCode), CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("not supported")), string.Join('\n', lines));
        Assert.IsFalse(lines.Any(l => l.Contains("push rbp")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_ElfForAarch64_SaysItIsNotSupported()
    {
        IReadOnlyList<string> lines = DisassemblyBuilder.Build(BuildElf(183, SampleCode), CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("not supported")), string.Join('\n', lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_CodeOverLineLimit_IsCutAndSaysSo()
    {
        byte[] nops = new byte[DisassemblyBudget.MaxLines + 5_000];
        Array.Fill(nops, (byte)0x90);

        IReadOnlyList<string> lines = DisassemblyBuilder.Build(BuildPe(0x8664, nops), CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("output truncated")), "A listing past the line limit must say it was cut.");
        Assert.IsTrue(lines.Count <= DisassemblyBudget.MaxLines + 1, $"{lines.Count} lines.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_PastTheTimeBudget_IsCutAndSaysSo()
    {
        IReadOnlyList<string> lines = DisassemblyBuilder.Build(
            BuildPe(0x8664, new byte[1000]), CancellationToken.None, TimeSpan.FromMilliseconds(-1));

        Assert.IsTrue(lines.Any(l => l.Contains("time limit")), string.Join('\n', lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_CancelledToken_Throws()
    {
        using CancellationTokenSource source = new();
        source.Cancel();

        bool cancelled = false;
        try
        {
            DisassemblyBuilder.Build(BuildPe(0x8664, SampleCode), source.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Assert.IsTrue(cancelled);
    }

    // ── malformed input ─────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Build_AnyTruncationOfAnAssemblyPeOrElf_NeverThrows()
    {
        byte[][] samples = [BuildManagedAssembly(0), BuildPe(0x8664, SampleCode), BuildElf(62, SampleCode)];
        foreach (byte[] sample in samples)
        {
            for (int length = 0; length <= sample.Length; length++)
            {
                IReadOnlyList<string> lines = DisassemblyBuilder.Build(sample[..length], CancellationToken.None);
                Assert.IsNotNull(lines);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_CorruptedBytes_NeverThrows()
    {
        byte[][] samples = [BuildManagedAssembly(0), BuildPe(0x8664, SampleCode), BuildElf(62, SampleCode)];
        Random random = new(1234);
        foreach (byte[] sample in samples)
        {
            for (int round = 0; round < 200; round++)
            {
                byte[] damaged = (byte[])sample.Clone();
                for (int hit = 0; hit < 4; hit++)
                {
                    damaged[random.Next(damaged.Length)] = (byte)random.Next(256);
                }

                Assert.IsNotNull(DisassemblyBuilder.Build(damaged, CancellationToken.None));
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_Garbage_SaysThereIsNothingToDisassemble()
    {
        IReadOnlyList<string> lines = DisassemblyBuilder.Build(Encoding.ASCII.GetBytes("this is not a binary"), CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("nothing to disassemble")), string.Join('\n', lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_SwitchWithHugeCount_DoesNotRunPastTheMethod()
    {
        // A switch whose declared target count is rewritten to 0xFFFFFFFF, with only one target behind it.
        byte[] hostile = BuildManagedAssembly(0, il =>
        {
            Label target = il.DefineLabel();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Switch, [target]);
            il.MarkLabel(target);
        });
        int switchOffset = IndexOf(hostile, [0x45, 0x01, 0x00, 0x00, 0x00]);
        Assert.IsTrue(switchOffset >= 0, "The emitted switch was not found.");
        BinaryPrimitives.WriteUInt32LittleEndian(hostile.AsSpan(switchOffset + 1), 0xFFFFFFFF);

        IReadOnlyList<string> lines = DisassemblyBuilder.Build(hostile, CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("operand runs past the end")), string.Join('\n', lines));
    }

    // ── builders ────────────────────────────────────────────────────────────

    private static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);

    private static byte[] BuildManagedAssembly(int extraNops, Action<ILGenerator>? body = null)
    {
        PersistedAssemblyBuilder assembly = new(new AssemblyName("Demo"), typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("Demo");
        TypeBuilder type = module.DefineType("Demo.Greeter", TypeAttributes.Public | TypeAttributes.Class);
        MethodBuilder method = type.DefineMethod("Hello", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        if (body is not null)
        {
            body(il);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "hello world");
            il.Emit(OpCodes.Call, typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])!);
        }

        for (int i = 0; i < extraNops; i++)
        {
            il.Emit(OpCodes.Nop);
        }

        il.Emit(OpCodes.Ret);
        type.CreateType();
        using MemoryStream stream = new();
        assembly.Save(stream);
        return stream.ToArray();
    }

    /// <summary>A minimal PE32+ (or PE32+ with another machine) with one executable section at RVA 0x1000, image base 0x140000000.</summary>
    private static byte[] BuildPe(ushort machine, byte[] code)
    {
        const int peOffset = 0x40;
        const int optionalSize = 240;
        const int rawOffset = 0x200;
        byte[] file = new byte[rawOffset + code.Length];
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x3C), peOffset);
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(file, peOffset);
        Span<byte> coff = file.AsSpan(peOffset + 4);
        BinaryPrimitives.WriteUInt16LittleEndian(coff, machine);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[16..], optionalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[18..], 0x22);
        Span<byte> optional = file.AsSpan(peOffset + 24);
        BinaryPrimitives.WriteUInt16LittleEndian(optional, 0x20B);
        BinaryPrimitives.WriteUInt32LittleEndian(optional[16..], 0x1000);
        BinaryPrimitives.WriteUInt64LittleEndian(optional[24..], 0x140000000);
        BinaryPrimitives.WriteUInt32LittleEndian(optional[32..], 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(optional[36..], 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(optional[56..], 0x1000 + (uint)code.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(optional[60..], rawOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[68..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(optional[108..], 16);
        Span<byte> section = file.AsSpan(peOffset + 24 + optionalSize);
        Encoding.ASCII.GetBytes(".text").CopyTo(section);
        BinaryPrimitives.WriteUInt32LittleEndian(section[8..], (uint)code.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(section[12..], 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(section[16..], (uint)code.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(section[20..], rawOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(section[36..], 0x60000020);
        code.CopyTo(file, rawOffset);
        return file;
    }

    /// <summary>A minimal little-endian ELF64 with a .text section (address 0x401000) and a section name table.</summary>
    private static byte[] BuildElf(ushort machine, byte[] code)
    {
        const int codeOffset = 0x40;
        int namesOffset = codeOffset + code.Length;
        byte[] names = Encoding.ASCII.GetBytes("\0.text\0.shstrtab\0");
        int tableOffset = (namesOffset + names.Length + 7) & ~7;
        byte[] file = new byte[tableOffset + (3 * 64)];
        file[0] = 0x7F;
        file[1] = (byte)'E';
        file[2] = (byte)'L';
        file[3] = (byte)'F';
        file[4] = 2;
        file[5] = 1;
        file[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x10), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x12), machine);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x14), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0x28), (ulong)tableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x34), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3A), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3C), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x3E), 2);
        code.CopyTo(file, codeOffset);
        names.CopyTo(file, namesOffset);
        Span<byte> text = file.AsSpan(tableOffset + 64);
        BinaryPrimitives.WriteUInt32LittleEndian(text, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(text[4..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(text[8..], 6);
        BinaryPrimitives.WriteUInt64LittleEndian(text[0x10..], 0x401000);
        BinaryPrimitives.WriteUInt64LittleEndian(text[0x18..], codeOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(text[0x20..], (ulong)code.Length);
        Span<byte> strings = file.AsSpan(tableOffset + 128);
        BinaryPrimitives.WriteUInt32LittleEndian(strings, 7);
        BinaryPrimitives.WriteUInt32LittleEndian(strings[4..], 3);
        BinaryPrimitives.WriteUInt64LittleEndian(strings[0x18..], (ulong)namesOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(strings[0x20..], (ulong)names.Length);
        return file;
    }
}
