using iConsole.Executables;

namespace iConsole.Test.Executables;

[TestClass]
public class ExecutableFileTypesTest
{
    [TestMethod]
    [DataRow("/bin/ls", true)]
    [DataRow("C:\\Windows\\notepad.exe", true)]
    [DataRow("a.DLL", true)]
    [DataRow("/usr/lib/libc.so.6", true)]
    [DataRow("libfoo.dylib", true)]
    [DataRow("/usr/lib/libz.so", true)]
    [DataRow("notes.txt", false)]
    [DataRow("photo.jpg", false)]
    [DataRow("data.json", false)]
    [DataRow("", false)]
    public void IsCandidate_FiltersByEndingOrNoEnding(string path, bool expected)
    {
        Assert.AreEqual(expected, ExecutableFileTypes.IsCandidate(path));
    }

    [TestMethod]
    [DataRow(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, true, DisplayName = "PE")]
    [DataRow(new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, true, DisplayName = "ELF")]
    [DataRow(new byte[] { 0xCF, 0xFA, 0xED, 0xFE }, true, DisplayName = "Mach-O 64 little-endian")]
    [DataRow(new byte[] { 0xCE, 0xFA, 0xED, 0xFE }, true, DisplayName = "Mach-O 32 little-endian")]
    [DataRow(new byte[] { 0xFE, 0xED, 0xFA, 0xCF }, true, DisplayName = "Mach-O 64 big-endian")]
    [DataRow(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0, 0, 0, 2 }, true, DisplayName = "universal binary")]
    [DataRow(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0, 0, 0, 0x34 }, false, DisplayName = "Java class")]
    [DataRow(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, false, DisplayName = "cut short before the slice count")]
    [DataRow(new byte[] { 0x23, 0x21, 0x2F, 0x62 }, false, DisplayName = "script")]
    [DataRow(new byte[] { 0x4D }, false, DisplayName = "one byte")]
    [DataRow(new byte[0], false, DisplayName = "empty")]
    public void HasSignature_RecognisesTheMagicNumbers(byte[] bytes, bool expected)
    {
        Assert.AreEqual(expected, ExecutableFileTypes.HasSignature(bytes));
    }

    [TestMethod]
    public void FileHasSignature_ReadsTheStartOfALocalFile()
    {
        string good = Path.Combine(Path.GetTempPath(), $"ExeTypes_{Guid.NewGuid():N}.exe");
        string bad = Path.Combine(Path.GetTempPath(), $"ExeTypes_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(good, [0x4D, 0x5A, 0, 0, 0, 0, 0, 0]);
        File.WriteAllText(bad, "echo hello");
        try
        {
            Assert.IsTrue(ExecutableFileTypes.FileHasSignature(good));
            Assert.IsFalse(ExecutableFileTypes.FileHasSignature(bad));
            Assert.IsFalse(ExecutableFileTypes.FileHasSignature(good + ".missing"));
        }
        finally
        {
            File.Delete(good);
            File.Delete(bad);
        }
    }
}
