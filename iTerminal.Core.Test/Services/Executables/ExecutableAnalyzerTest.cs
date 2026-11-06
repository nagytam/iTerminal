using System.Diagnostics;
using iConsole.Executables;
using iTerminal.Core.Services.Executables;

namespace iTerminal.Core.Test.Services.Executables;

[TestClass]
public class ExecutableAnalyzerTest
{
    private static readonly ExecutableAnalyzer Analyzer = new();

    private static ExecutableReport Analyze(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ExeAnalyzerTest_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        try
        {
            return Analyzer.Analyze(path, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool? Flag(ExecutableReport report, string name) => report.Hardening.Single(f => f.Name == name).Enabled;

    // ── ELF ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Elf_HardenedPieExecutable_ReportsEveryProtection()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf64());

        Assert.AreEqual(ExecutableFormat.Elf, report.Format);
        Assert.AreEqual("ELF64 little-endian", report.FormatName);
        Assert.AreEqual("x86-64", report.Architecture);
        Assert.AreEqual("Linux", report.Subsystem);
        Assert.AreEqual(ExecutableKind.PieExecutable, report.Kind);
        Assert.AreEqual(true, Flag(report, "PIE"));
        Assert.AreEqual(true, Flag(report, "NX (non-executable stack)"));
        Assert.AreEqual(true, Flag(report, "RELRO"));
        Assert.AreEqual(true, Flag(report, "Full RELRO (BIND_NOW)"));
        Assert.AreEqual(true, Flag(report, "Stack canary"));
    }

    [TestMethod]
    public void Elf_UnhardenedExecutable_ReportsEveryProtectionOff()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf64(new ExecutableFixtures.ElfOptions
        {
            Pie = false, NonExecutableStack = false, Relro = false, BindNow = false, Canary = false, Interpreter = false,
        }));

        Assert.AreEqual(ExecutableKind.Executable, report.Kind);
        Assert.AreEqual(false, Flag(report, "PIE"));
        Assert.AreEqual(false, Flag(report, "NX (non-executable stack)"));
        Assert.AreEqual(false, Flag(report, "RELRO"));
        Assert.AreEqual(false, Flag(report, "Full RELRO (BIND_NOW)"));
        Assert.AreEqual(false, Flag(report, "Stack canary"));
    }

    [TestMethod]
    public void Elf_PartialRelro_IsNotFullRelro()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf64(new ExecutableFixtures.ElfOptions { BindNow = false }));

        Assert.AreEqual(true, Flag(report, "RELRO"));
        Assert.AreEqual(false, Flag(report, "Full RELRO (BIND_NOW)"));
    }

    [TestMethod]
    public void Elf_ListsLibrariesSymbolsAndSections()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf64());

        Assert.AreEqual("libc.so.6", report.Imports.Single().Library);
        Assert.AreEqual(1, report.ImportedSymbols);
        Assert.AreEqual(1, report.ExportedSymbols);
        CollectionAssert.AreEqual(new[] { "main" }, report.Exports.ToArray());
        CollectionAssert.IsSubsetOf(new[] { ".text", ".dynsym", ".dynamic" }, report.Sections.Select(s => s.Name).ToArray());
        Assert.IsTrue(report.Sections.Single(s => s.Name == ".text").Executable);
    }

    [TestMethod]
    public void Elf_WithoutSectionHeaders_DescribesTheLoadSegmentsAndCannotTellTheCanary()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf64(new ExecutableFixtures.ElfOptions { SectionHeaders = false }));

        Assert.AreEqual("LOAD[0]", report.Sections.Single().Name);
        Assert.IsNull(Flag(report, "Stack canary"));
    }

    [TestMethod]
    public void Elf_BigEndian32Bit_IsReadWithTheRightByteOrder()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf32BigEndianHeaderOnly());

        Assert.AreEqual("ELF32 big-endian", report.FormatName);
        Assert.AreEqual("PowerPC", report.Architecture);
        Assert.AreEqual(ExecutableKind.Executable, report.Kind);
        Assert.AreEqual("0x10000400", report.EntryPoint);
    }

    [TestMethod]
    public void Elf_RandomCode_IsFlaggedAsHighEntropy()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf64(new ExecutableFixtures.ElfOptions { Code = ExecutableFixtures.RandomBytes(8192) }));

        ExecutableSection text = report.Sections.Single(s => s.Name == ".text");
        Assert.IsTrue(text.Entropy > 7.5, $"entropy was {text.Entropy}");
        Assert.IsTrue(report.Hints.Any(h => h.Kind == ExecutableHintKind.HighEntropySection && h.Argument == ".text"));
    }

    [TestMethod]
    public void Elf_PlainCode_IsNotFlagged()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf64());

        Assert.IsFalse(report.Hints.Any(h => h.Kind == ExecutableHintKind.HighEntropySection));
    }

    [TestMethod]
    public void Elf_PackerSectionName_IsFlagged()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf64(new ExecutableFixtures.ElfOptions { TextSectionName = "UPX0" }));

        Assert.IsTrue(report.Hints.Any(h => h.Kind == ExecutableHintKind.PackerSectionName && h.Argument == "UPX0"));
    }

    [TestMethod]
    public void Elf_VeryLargeSection_HasItsEntropySampled()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Elf64(new ExecutableFixtures.ElfOptions { Code = new byte[40 * 1024 * 1024] }));

        Assert.IsTrue(report.Sections.Single(s => s.Name == ".text").Sampled);
        Assert.IsTrue(report.Hints.Any(h => h.Kind == ExecutableHintKind.EntropySampled));
    }

    // ── PE ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Pe_Hardened64BitImage_ReportsEveryProtection()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Pe());

        Assert.AreEqual(ExecutableFormat.PE, report.Format);
        Assert.AreEqual("PE32+ (64-bit)", report.FormatName);
        Assert.AreEqual("x86-64", report.Architecture);
        Assert.AreEqual("Windows console", report.Subsystem);
        Assert.AreEqual(ExecutableKind.Executable, report.Kind);
        Assert.AreEqual(new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc), report.Timestamp);
        Assert.AreEqual(true, Flag(report, "ASLR"));
        Assert.AreEqual(true, Flag(report, "High-entropy ASLR"));
        Assert.AreEqual(true, Flag(report, "DEP/NX"));
        Assert.AreEqual(true, Flag(report, "CFG"));
        Assert.IsNull(Flag(report, "SafeSEH"), "SafeSEH does not exist in a 64-bit image");
        Assert.AreEqual(true, Flag(report, "Stack cookie (/GS)"));
    }

    [TestMethod]
    public void Pe_WithoutProtections_ReportsThemOff()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { DllCharacteristics = 0, SecurityCookie = false }));

        Assert.AreEqual(false, Flag(report, "ASLR"));
        Assert.AreEqual(false, Flag(report, "High-entropy ASLR"));
        Assert.AreEqual(false, Flag(report, "DEP/NX"));
        Assert.AreEqual(false, Flag(report, "CFG"));
        Assert.AreEqual(false, Flag(report, "Stack cookie (/GS)"));
    }

    [TestMethod]
    public void Pe_AslrNeedsRelocations()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { Characteristics = 0x0023 }));

        Assert.AreEqual(false, Flag(report, "ASLR"), "an image whose relocations were stripped cannot move");
    }

    [TestMethod]
    public void Pe_32BitImage_ChecksSafeSehAgainstTheHandlerTable()
    {
        ExecutableReport without = Analyze(ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { Is64 = false, DllCharacteristics = 0x0140 }));
        ExecutableReport with = Analyze(ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { Is64 = false, DllCharacteristics = 0x0140, SafeSehTable = true }));
        ExecutableReport noSeh = Analyze(ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { Is64 = false, DllCharacteristics = 0x0540 }));

        Assert.AreEqual("PE32 (32-bit)", with.FormatName);
        Assert.AreEqual("x86", with.Architecture);
        Assert.AreEqual(false, Flag(without, "SafeSEH"));
        Assert.AreEqual(true, Flag(with, "SafeSEH"));
        Assert.AreEqual(true, Flag(noSeh, "SafeSEH"), "an image that declares no SEH has no unsafe handler");
        Assert.IsNull(Flag(with, "High-entropy ASLR"));
        Assert.AreEqual(true, Flag(with, "Stack cookie (/GS)"));
    }

    [TestMethod]
    public void Pe_ListsImportsAndExports()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Pe());

        ExecutableImport import = report.Imports.Single();
        Assert.AreEqual("kernel32.dll", import.Library);
        Assert.AreEqual(2, import.Functions);
        Assert.AreEqual(2, report.ImportedSymbols);
        Assert.AreEqual(2, report.ExportedSymbols);
        CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, report.Exports.ToArray());
    }

    [TestMethod]
    public void Pe_ListsSectionsWithAccessAndEntropy()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Pe());

        ExecutableSection text = report.Sections.Single(s => s.Name == ".text");
        Assert.IsTrue(text.Executable);
        Assert.IsFalse(text.Writable);
        Assert.IsTrue(text.Entropy < 7.0);
        Assert.AreEqual(2, report.Sections.Count);
    }

    [TestMethod]
    public void Pe_WritableExecutableAndPackedSection_IsFlagged()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions
        {
            TextName = "UPX0", TextFlags = 0xE0000020, Code = ExecutableFixtures.RandomBytes(0x200),
        }));

        Assert.IsTrue(report.Hints.Any(h => h.Kind == ExecutableHintKind.WritableExecutableSection));
        Assert.IsTrue(report.Hints.Any(h => h.Kind == ExecutableHintKind.PackerSectionName));
        Assert.IsTrue(report.Hints.Any(h => h.Kind == ExecutableHintKind.HighEntropySection));
    }

    [TestMethod]
    public void Pe_TimestampInTheFuture_IsNotShown()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { TimeDateStamp = 0xFFFFFFF0 }));

        Assert.IsNull(report.Timestamp);
        Assert.IsTrue(report.Hints.Any(h => h.Kind == ExecutableHintKind.TimestampUnreliable));
    }

    [TestMethod]
    public void Pe_UnsignedImage_HasNoSignature()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Pe());

        Assert.IsFalse(report.Signed);
        Assert.AreEqual(0, report.Certificates.Count);
    }

    [TestMethod]
    public void Pe_EmbeddedSignature_ListsTheCertificate()
    {
        byte[] blob = ExecutableFixtures.SignedDataWithCertificate("CN=Test Publisher, O=iTerminal", out string thumbprint);

        ExecutableReport report = Analyze(ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { Certificate = blob }));

        Assert.IsTrue(report.Signed);
        ExecutableCertificate certificate = report.Certificates.Single();
        Assert.AreEqual("CN=Test Publisher, O=iTerminal", certificate.Subject);
        Assert.AreEqual(certificate.Subject, certificate.Issuer, "self-signed");
        Assert.AreEqual(thumbprint, certificate.Thumbprint);
        Assert.AreEqual(2024, certificate.NotBefore.Year);
        Assert.AreEqual(2034, certificate.NotAfter.Year);
    }

    [TestMethod]
    public void Pe_SignatureThatIsNotPkcs7_IsSignedWithoutCertificates()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { Certificate = ExecutableFixtures.RandomBytes(300) }));

        Assert.IsTrue(report.Signed);
        Assert.AreEqual(0, report.Certificates.Count);
    }

    [TestMethod]
    public void Pe_RealManagedAssembly_IsReadAsManaged()
    {
        ExecutableReport report = Analyzer.Analyze(typeof(object).Assembly.Location, CancellationToken.None);

        Assert.AreEqual(ExecutableFormat.PE, report.Format);
        Assert.IsTrue(report.Managed);
        Assert.AreEqual(ExecutableKind.SharedLibrary, report.Kind);
        Assert.IsTrue(report.Sections.Count > 0);
    }

    [TestMethod]
    public void RealHostProgram_IsReadWithoutError()
    {
        string path = Environment.ProcessPath!;

        ExecutableReport report = Analyzer.Analyze(path, CancellationToken.None);

        Assert.AreEqual(OperatingSystem.IsWindows() ? ExecutableFormat.PE : OperatingSystem.IsMacOS() ? ExecutableFormat.MachO : ExecutableFormat.Elf, report.Format);
        Assert.IsTrue(report.Sections.Count > 0);
        Assert.IsTrue(report.Hardening.Count > 0);
    }

    // ── Mach-O ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MachO_Executable_ReportsProtectionsSignatureAndLibraries()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.MachO64());

        Assert.AreEqual(ExecutableFormat.MachO, report.Format);
        Assert.AreEqual("ARM64", report.Architecture);
        Assert.AreEqual(ExecutableKind.PieExecutable, report.Kind);
        Assert.AreEqual(true, Flag(report, "PIE"));
        Assert.AreEqual(true, Flag(report, "NX stack"));
        Assert.AreEqual(true, Flag(report, "Stack canary"));
        Assert.IsTrue(report.Signed);
        Assert.AreEqual("/usr/lib/libSystem.B.dylib", report.Imports.Single().Library);
        Assert.AreEqual(1, report.ImportedSymbols);
        CollectionAssert.AreEqual(new[] { "_main" }, report.Exports.ToArray());
        Assert.AreEqual("__TEXT,__text", report.Sections.Single().Name);
        Assert.IsTrue(report.Sections.Single().Executable);
    }

    [TestMethod]
    public void MachO_WithoutProtections_ReportsThemOff()
    {
        ExecutableReport report = Analyze(ExecutableFixtures.MachO64(new ExecutableFixtures.MachOptions { Pie = false, Canary = false, CodeSignature = false }));

        Assert.AreEqual(false, Flag(report, "PIE"));
        Assert.AreEqual(false, Flag(report, "Stack canary"));
        Assert.IsFalse(report.Signed);
    }

    [TestMethod]
    public void MachO_UniversalBinary_ListsTheSlicesAndReadsTheFirst()
    {
        byte[] thin = ExecutableFixtures.MachO64();

        ExecutableReport report = Analyze(ExecutableFixtures.Fat(thin, thin));

        Assert.AreEqual("x86-64, ARM64", report.Architecture);
        StringAssert.StartsWith(report.FormatName, "Mach-O universal (2 slices)");
        Assert.AreEqual(1, report.Sections.Count);
    }

    // ── Not an executable, damaged and hostile files ───────────────────────────

    [TestMethod]
    [DataRow(new byte[0])]
    [DataRow(new byte[] { 0x4D })]
    [DataRow(new byte[] { 0x23, 0x21, 0x2F, 0x62, 0x69, 0x6E, 0x2F, 0x73, 0x68, 0x0A })]
    [DataRow(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x34 })]
    public void NotAnExecutable_RaisesTheAnalysisException(byte[] bytes)
    {
        Assert.ThrowsExactly<ExecutableAnalysisException>(() => Analyze(bytes));
    }

    [TestMethod]
    public void ACancelledToken_StopsTheRead()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ExeAnalyzerTest_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, ExecutableFixtures.Elf64());
        try
        {
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.ThrowsExactly<OperationCanceledException>(() => Analyzer.Analyze(path, source.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static IEnumerable<object[]> Fixtures()
    {
        yield return ["ELF", ExecutableFixtures.Elf64()];
        yield return ["PE64", ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { Certificate = ExecutableFixtures.SignedDataWithCertificate("CN=Fuzz", out _) })];
        yield return ["PE32", ExecutableFixtures.Pe(new ExecutableFixtures.PeOptions { Is64 = false })];
        yield return ["MachO", ExecutableFixtures.MachO64()];
        yield return ["Fat", ExecutableFixtures.Fat(ExecutableFixtures.MachO64())];
    }

    /// <summary>Runs the analyzer on a damaged file: only the analysis exception may come out, quickly and without a big allocation.</summary>
    private static void AssertSurvives(string name, byte[] bytes, string what)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ExeFuzz_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        try
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            try
            {
                Analyzer.Analyze(path, CancellationToken.None);
            }
            catch (ExecutableAnalysisException)
            {
                // A refusal is a good answer.
            }
            catch (Exception ex)
            {
                Assert.Fail($"{name} {what}: {ex.GetType().Name}: {ex.Message}");
            }

            Assert.IsTrue(watch.ElapsedMilliseconds < 20000, $"{name} {what} took {watch.ElapsedMilliseconds} ms");
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(allocated < 64L * 1024 * 1024, $"{name} {what} allocated {allocated:N0} bytes");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [Timeout(600_000)]
    public void EveryTruncation_OfEveryFormat_IsRefusedCleanly()
    {
        foreach (object[] fixture in Fixtures())
        {
            string name = (string)fixture[0];
            byte[] bytes = (byte[])fixture[1];
            // Every length through the main header, then a step through the rest.
            for (int length = 0; length < bytes.Length; length += length < 128 ? 1 : 29)
            {
                AssertSurvives(name, bytes[..length], $"cut to {length}");
            }
        }
    }

    [TestMethod]
    [Timeout(600_000)]
    public void SeededCorruption_OfEveryFormat_NeverEscapesAsAnotherException()
    {
        foreach (object[] fixture in Fixtures())
        {
            string name = (string)fixture[0];
            byte[] original = (byte[])fixture[1];
            var random = new Random(12345);
            for (int round = 0; round < 100; round++)
            {
                byte[] bytes = [.. original];
                int flips = random.Next(1, 9);
                for (int i = 0; i < flips; i++)
                {
                    // Half the damage lands in the first 600 bytes, where the headers are; half anywhere.
                    int at = random.Next(0, random.Next(2) == 0 ? Math.Min(600, bytes.Length) : bytes.Length);
                    bytes[at] = (byte)random.Next(256);
                }

                // Counts and offsets are the dangerous fields: also write a huge 32-bit value at a random header position.
                if (round % 4 == 0)
                {
                    int at = random.Next(0, Math.Min(600, bytes.Length - 4));
                    BitConverter.TryWriteBytes(bytes.AsSpan(at), 0xFFFFFFFFu);
                }

                AssertSurvives(name, bytes, $"round {round}");
            }
        }
    }

    [TestMethod]
    public void ElfWithHugeCounts_IsCutAndBounded()
    {
        byte[] bytes = ExecutableFixtures.Elf64();
        BitConverter.TryWriteBytes(bytes.AsSpan(56), (ushort)0xFFFF);
        BitConverter.TryWriteBytes(bytes.AsSpan(60), (ushort)0xFFFF);

        AssertSurvives("ELF", bytes, "65535 headers");
    }

    [TestMethod]
    public void MachOWithHugeCommandCounts_IsCutAndBounded()
    {
        byte[] bytes = ExecutableFixtures.MachO64();
        BitConverter.TryWriteBytes(bytes.AsSpan(16), 0xFFFFFFFFu);
        BitConverter.TryWriteBytes(bytes.AsSpan(20), 0xFFFFFFFFu);

        AssertSurvives("MachO", bytes, "4 billion commands");
    }

    [TestMethod]
    public void PeWithAnImportTableOfJunk_IsBounded()
    {
        byte[] bytes = ExecutableFixtures.Pe();
        Array.Fill(bytes, (byte)1, 0x400, 0x3FF);

        AssertSurvives("PE", bytes, "junk imports");
    }
}
