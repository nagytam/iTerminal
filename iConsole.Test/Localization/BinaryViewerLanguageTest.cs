using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Services;
using Moq;

namespace iConsole.Test.Localization;

[TestClass]
public class BinaryViewerLanguageTest
{
    private static readonly string[] ResourceKeys =
    [
        "Viewer.File.CouldNotRead", "Viewer.File.AccessLocalOnly", "Viewer.File.LinkedDocumentOutsideFolder",
        "Viewer.File.LinkedDocumentMissing", "Viewer.File.NestedMasterNotExpanded", "Viewer.File.FileVersionInfoUnavailable",
        "Viewer.File.AssemblyIdentityUnavailable", "Viewer.BinaryMetadata.File", "Viewer.BinaryMetadata.Name",
        "Viewer.BinaryMetadata.Size", "Viewer.BinaryMetadata.Bytes", "Viewer.BinaryMetadata.FileVersionInfo",
        "Viewer.BinaryMetadata.FileDescription", "Viewer.BinaryMetadata.FileVersion", "Viewer.BinaryMetadata.ProductName",
        "Viewer.BinaryMetadata.ProductVersion", "Viewer.BinaryMetadata.CompanyName", "Viewer.BinaryMetadata.LegalCopyright",
        "Viewer.BinaryMetadata.LegalTrademarks", "Viewer.BinaryMetadata.Comments", "Viewer.BinaryMetadata.InternalName",
        "Viewer.BinaryMetadata.OriginalFilename", "Viewer.BinaryMetadata.Language", "Viewer.BinaryMetadata.AssemblyIdentity",
        "Viewer.BinaryMetadata.AssemblyName", "Viewer.BinaryMetadata.Version", "Viewer.BinaryMetadata.Culture",
        "Viewer.BinaryMetadata.PublicKeyToken", "Viewer.BinaryMetadata.ContentType", "Viewer.BinaryMetadata.Flags",
        "Viewer.BinaryMetadata.NotManagedAssembly", "Viewer.Disassembly.IlHeading", "Viewer.Disassembly.NativeHeading",
        "Viewer.Disassembly.NotExecutable", "Viewer.Disassembly.HeadersUnreadable", "Viewer.Disassembly.PeImageMachine",
        "Viewer.Disassembly.UnsupportedMachine", "Viewer.Disassembly.SectionDataOutsideFile", "Viewer.Disassembly.ElfHeaderShort",
        "Viewer.Disassembly.ElfUnknownClass", "Viewer.Disassembly.BigEndianNotSupported", "Viewer.Disassembly.ElfImageMachine",
        "Viewer.Disassembly.NoSectionHeaders", "Viewer.Disassembly.MalformedSectionHeaders",
        "Viewer.Disassembly.SectionHeadersPastEnd", "Viewer.Disassembly.DecodedCodeLimit", "Viewer.Disassembly.Stopped",
        "Viewer.Disassembly.MetadataUnreadable", "Viewer.Disassembly.NoMethodBodies", "Viewer.Disassembly.MethodBodyUnreadable",
        "Viewer.Disassembly.MethodInstructionLimit", "Viewer.Disassembly.TruncatedMarker",
        "Viewer.Disassembly.OperandPastMethod", "Viewer.Disassembly.OutputTruncated", "Viewer.Disassembly.MethodLimit",
        "Viewer.Disassembly.LineLimit", "Viewer.Disassembly.TimeLimit",
    ];

    private static readonly HashSet<string> KeysThatMayMatchEnglish =
    [
        "Viewer.BinaryMetadata.File",
        "Viewer.BinaryMetadata.Name",
        "Viewer.BinaryMetadata.Bytes",
        "Viewer.BinaryMetadata.Version",
        "Viewer.BinaryMetadata.Culture",
        "Viewer.BinaryMetadata.Flags",
    ];

    private static readonly string TestFilesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Services", "TestFiles");

    private IDisposable? _language;

    [TestCleanup]
    public void Reset() => _language?.Dispose();

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryBinaryViewerString()
    {
        Localizer english = new();
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(language => language.Code != "en"))
        {
            Localizer localizer = new();
            Assert.IsTrue(localizer.SetLanguage(language.Code), language.Code);
            foreach (string key in ResourceKeys)
            {
                Assert.IsTrue(english.TryGet(key, out string englishText), key);
                Assert.IsTrue(localizer.TryGet(key, out string translatedText), language.Code + " " + key);
                if (!KeysThatMayMatchEnglish.Contains(key))
                {
                    Assert.AreNotEqual(englishText, translatedText, language.Code + " " + key);
                }
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EveryLanguage_LocalizesBinaryMetadataAndDisassemblyOutput()
    {
        string invalidBinaryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".so");
        await File.WriteAllBytesAsync(invalidBinaryPath, "not an executable"u8.ToArray());
        try
        {
            Url accessUrl = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mdb"));
            Mock<IFileSystem> fileSystem = new();
            fileSystem.Setup(fs => fs.ExistsFile(accessUrl)).Returns(true);

            FileToTextConverterService service = new();
            foreach (LanguageInfo language in Localizer.SupportedLanguages)
            {
                Localizer localizer = new();
                Assert.IsTrue(localizer.SetLanguage(language.Code), language.Code);
                _language?.Dispose();
                _language = UiText.Scope(localizer);

                TextModel metadata = new();
                await service.ReadFileAsync(
                    new Url(Path.Combine(TestFilesDir, "sample.dll")),
                    new LocalFileSystemAdapter(),
                    metadata);

                AssertHeading(metadata.Lines, "BinaryMetadata.File", "File");
                AssertField(metadata.Lines, "BinaryMetadata.Name", "Name");
                string sizeLabel = UiText.Get("Viewer.BinaryMetadata.Size", "Size");
                long fileSize = new FileInfo(Path.Combine(TestFilesDir, "sample.dll")).Length;
                string sizeValue = localizer.Get("Viewer.BinaryMetadata.Bytes", fileSize);
                Assert.IsTrue(
                    metadata.Lines.Contains($"  {sizeLabel,-22}{sizeValue}"),
                    string.Join('\n', metadata.Lines));
                AssertHeading(metadata.Lines, "BinaryMetadata.FileVersionInfo", "File Version Info");
                AssertHeading(metadata.Lines, "BinaryMetadata.AssemblyIdentity", "Assembly Identity");
                AssertField(metadata.Lines, "BinaryMetadata.AssemblyName", "Assembly Name");

                IReadOnlyList<string> managedDisassembly = await service.BuildDisassemblyAsync(
                    new Url(Path.Combine(TestFilesDir, "sample.dll")),
                    new LocalFileSystemAdapter(),
                    CancellationToken.None);
                AssertHeading(managedDisassembly, "Disassembly.IlHeading", "Disassembly (IL)");

                IReadOnlyList<string> nativeDisassembly = await service.BuildDisassemblyAsync(
                    new Url(Path.Combine(TestFilesDir, "sample.exe")),
                    new LocalFileSystemAdapter(),
                    CancellationToken.None);
                AssertHeading(nativeDisassembly, "Disassembly.NativeHeading", "Disassembly (native)");

                IReadOnlyList<string> invalidDisassembly = await service.BuildDisassemblyAsync(
                    new Url(invalidBinaryPath),
                    new LocalFileSystemAdapter(),
                    CancellationToken.None);
                Assert.IsTrue(
                    invalidDisassembly.Contains(UiText.Get(
                        "Viewer.Disassembly.NotExecutable",
                        "  Not a Windows PE or Linux ELF file; there is nothing to disassemble.")),
                    string.Join('\n', invalidDisassembly));

                TextModel accessNote = new();
                await service.ReadFileAsync(accessUrl, fileSystem.Object, accessNote);
                Assert.AreEqual(
                    $"[{UiText.Get("Viewer.File.AccessLocalOnly", "Access databases are read from a local file")}]",
                    accessNote.Lines.Single());
            }
        }
        finally
        {
            File.Delete(invalidBinaryPath);
        }
    }

    private static void AssertHeading(IEnumerable<string> lines, string key, string fallback)
    {
        string heading = UiText.Get("Viewer." + key, fallback);
        Assert.IsTrue(lines.Any(line => line.StartsWith($"── {heading} ", StringComparison.Ordinal)), string.Join('\n', lines.Take(5)));
    }

    private static void AssertField(IEnumerable<string> lines, string key, string fallback)
    {
        string label = UiText.Get("Viewer." + key, fallback);
        Assert.IsTrue(lines.Any(line => line.StartsWith($"  {label,-22}", StringComparison.Ordinal)), string.Join('\n', lines));
    }
}
