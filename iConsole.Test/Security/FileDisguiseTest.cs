using iConsole.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Security;

[TestClass]
[DoNotParallelize]
public class FileDisguiseTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, FileKind.Executable)]
    [DataRow(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02 }, FileKind.Executable)]
    [DataRow(new byte[] { 0xCF, 0xFA, 0xED, 0xFE }, FileKind.Executable)]
    [DataRow(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, FileKind.Pdf)]
    [DataRow(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, FileKind.Png)]
    [DataRow(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, FileKind.Jpeg)]
    [DataRow(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39 }, FileKind.Gif)]
    [DataRow(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, FileKind.Zip)]
    [DataRow(new byte[] { 0x68, 0x69 }, FileKind.Unknown)]
    [DataRow(new byte[] { }, FileKind.Unknown)]
    public void Detect_ReadsTheSignature(byte[] header, FileKind expected)
    {
        Assert.AreEqual(expected, FileDisguise.Detect(header));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("report.pdf", FileKind.Executable, true)]
    [DataRow("photo.JPG", FileKind.Executable, true)]
    [DataRow("notes.txt", FileKind.Executable, true)]
    [DataRow("photo.jpg", FileKind.Png, true)]
    [DataRow("report.pdf", FileKind.Zip, true)]
    [DataRow("photo.jpg", FileKind.Jpeg, false)]
    [DataRow("report.docx", FileKind.Zip, false)]
    [DataRow("setup.exe", FileKind.Executable, false)]
    [DataRow("libc.so", FileKind.Executable, false)]
    [DataRow("ls", FileKind.Executable, false)]
    [DataRow("tool-1.2", FileKind.Executable, false)]
    [DataRow("notes.txt", FileKind.Unknown, false)]
    [DataRow("report.pdf", FileKind.Unknown, false)]
    [DataRow("archive.zip", FileKind.Executable, true)]
    public void IsMismatch_FlagsContentThatContradictsTheName(string name, FileKind actual, bool expected)
    {
        Assert.AreEqual(expected, FileDisguise.IsMismatch(name, actual));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("invoice.pdf.exe", true)]
    [DataRow("photo.JPG.scr", true)]
    [DataRow("a.b.docx.bat", true)]
    [DataRow("invoice.exe", false)]
    [DataRow("archive.tar.gz", false)]
    [DataRow("report.v2.pdf", false)]
    [DataRow("setup.exe.txt", false)]
    [DataRow(".pdf.exe", true)]
    public void DoubleExtension_IsADocumentExtensionBeforeALaunchOne(string name, bool expected)
    {
        Assert.AreEqual(expected, FileDisguise.AnalyzeName(name).HasFlag(DisguiseFindings.DoubleExtension));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnalyzeName_FlagsRightToLeftOverrideAndControlCharacters()
    {
        string spoofed = "invoice" + (char)0x202E + "fdp.exe";
        Assert.IsTrue(FileDisguise.AnalyzeName(spoofed).HasFlag(DisguiseFindings.HiddenCharacters));
        Assert.IsTrue(FileDisguise.AnalyzeName("a" + (char)0x200B + "b.txt").HasFlag(DisguiseFindings.HiddenCharacters));
        Assert.IsTrue(FileDisguise.AnalyzeName("a" + (char)0x07 + "b.txt").HasFlag(DisguiseFindings.HiddenCharacters));
        Assert.AreEqual(DisguiseFindings.None, FileDisguise.AnalyzeName("Gr" + (char)0xF6 + (char)0xDF + "e " + (char)0x2014 + " " + (char)0x65E5 + ".txt"));
        Assert.AreEqual(DisguiseFindings.None, FileDisguise.AnalyzeName(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MaskAndReveal_ShowHiddenCharacters()
    {
        string spoofed = "a" + (char)0x202E + "b";
        Assert.AreEqual("a" + (char)0xB7 + "b", FileDisguise.Mask(spoofed));
        Assert.AreEqual("a<U+202E>b", FileDisguise.Reveal(spoofed));
        Assert.AreEqual("plain.txt", FileDisguise.Mask("plain.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Cache_FindingsCombineNameAndContent_AndStayBounded()
    {
        FileDisguiseCache.Clear();
        string path = Path.Combine(Path.GetTempPath(), "disguise-cache", "report.pdf");
        Assert.AreEqual(DisguiseFindings.None, FileDisguiseCache.Findings(path, "report.pdf"), "nothing is known until the bytes have been read");

        FileDisguiseCache.Set(path, FileKind.Executable);
        Assert.AreEqual(DisguiseFindings.TypeMismatch, FileDisguiseCache.Findings(path, "report.pdf"));

        for (int i = 0; i < FileDisguiseCache.Capacity + 5; i++)
        {
            FileDisguiseCache.Set("p" + i, FileKind.Pdf);
        }

        Assert.IsFalse(FileDisguiseCache.TryGet(path, out _), "a full cache is emptied");
        Assert.IsTrue(FileDisguiseCache.TryGet("p" + (FileDisguiseCache.Capacity + 4), out _));
        FileDisguiseCache.Clear();
    }
}
