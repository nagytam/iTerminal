using iConsole.Data;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Test.Services.Disassembly;

[TestClass]
public class FileToTextConverterDisassemblyTest
{
    private static readonly string TestFilesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Services", "TestFiles");

    [Timeout(60_000)]
    [TestMethod]
    public void HasDisassembly_IsTrueForTheBinaryExtensionsOnly()
    {
        Assert.IsTrue(FileToTextConverterService.HasDisassembly(new Url("file://C:/x/a.dll")));
        Assert.IsTrue(FileToTextConverterService.HasDisassembly(new Url("file://C:/x/a.EXE")));
        Assert.IsTrue(FileToTextConverterService.HasDisassembly(new Url("file://C:/x/liba.so")));
        Assert.IsFalse(FileToTextConverterService.HasDisassembly(new Url("file://C:/x/a.txt")));
        Assert.IsFalse(FileToTextConverterService.HasDisassembly(new Url("file://C:/x/liba.so.1")));
        Assert.IsFalse(FileToTextConverterService.HasDisassembly(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadFileAsync_ForAManagedDll_StaysMetadataOnly()
    {
        // The preview and the editor read through ReadFileAsync, so the disassembly must not be part of it.
        TextModel model = new();

        await new FileToTextConverterService().ReadFileAsync(SampleUrl("sample.dll"), new LocalFileSystemAdapter(), model);

        Assert.IsTrue(model.Lines.Any(l => l.Contains("Assembly Identity")));
        Assert.IsFalse(model.Lines.Any(l => l.Contains("Disassembly")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BuildDisassemblyAsync_ForAManagedDll_ListsIl()
    {
        IReadOnlyList<string> lines = await new FileToTextConverterService()
            .BuildDisassemblyAsync(SampleUrl("sample.dll"), new LocalFileSystemAdapter(), CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("Disassembly (IL)")), string.Join('\n', lines.Take(5)));
        Assert.IsTrue(lines.Any(l => l.Contains(".method")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BuildDisassemblyAsync_ForTheNativeApphostStub_ListsX64Code()
    {
        IReadOnlyList<string> lines = await new FileToTextConverterService()
            .BuildDisassemblyAsync(SampleUrl("sample.exe"), new LocalFileSystemAdapter(), CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("Disassembly (native)")), string.Join('\n', lines.Take(5)));
        Assert.IsTrue(lines.Any(l => l.Contains("section .text")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BuildDisassemblyAsync_ForOtherExtensions_ReturnsNothing()
    {
        IReadOnlyList<string> lines = await new FileToTextConverterService()
            .BuildDisassemblyAsync(new Url("file://C:/x/a.txt"), new LocalFileSystemAdapter(), CancellationToken.None);

        Assert.AreEqual(0, lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SoFileWithGarbageContent_StillShowsItsMetadataPage_AndDisassemblyDoesNotThrow()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".so");
        try
        {
            await File.WriteAllBytesAsync(path, "definitely not an ELF file"u8.ToArray());
            Url url = new(path);
            TextModel model = new();
            FileToTextConverterService service = new();

            await service.ReadFileAsync(url, new LocalFileSystemAdapter(), model);
            IReadOnlyList<string> lines = await service.BuildDisassemblyAsync(url, new LocalFileSystemAdapter(), CancellationToken.None);

            Assert.IsTrue(model.Lines.Any(l => l.Contains("File")), "The metadata page must still be there.");
            Assert.IsTrue(lines.Any(l => l.Contains("nothing to disassemble")), string.Join('\n', lines));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Url SampleUrl(string fileName) => new(Path.Combine(TestFilesDir, fileName));
}
