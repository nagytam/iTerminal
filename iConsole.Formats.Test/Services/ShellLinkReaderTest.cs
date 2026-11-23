using System.Text;
using iConsole.Formats.Services;

namespace iConsole.Formats.Test.Services;

/// <summary>Reading a Windows shortcut (MS-SHLLINK) as text, without resolving or running it.</summary>
[TestClass]
public class ShellLinkReaderTest
{
    private const uint HasLinkInfo = 1u << 1;
    private const uint HasName = 1u << 2;
    private const uint HasRelativePath = 1u << 3;
    private const uint HasWorkingDir = 1u << 4;
    private const uint HasArguments = 1u << 5;
    private const uint HasIconLocation = 1u << 6;
    private const uint IsUnicode = 1u << 7;

    /// <summary>A shortcut with a header and string data only, as the sample in Test/FileTypes has.</summary>
    private static byte[] Link(uint flags, params string[] strings)
    {
        byte[] header = new byte[0x4C];
        BitConverter.GetBytes(0x4Cu).CopyTo(header, 0);
        BitConverter.GetBytes(flags).CopyTo(header, 0x14);
        BitConverter.GetBytes(0x20u).CopyTo(header, 0x18);
        BitConverter.GetBytes(1u).CopyTo(header, 0x3C);
        List<byte> result = [.. header];
        foreach (string text in strings)
        {
            result.AddRange(BitConverter.GetBytes((ushort)text.Length));
            result.AddRange((flags & IsUnicode) != 0 ? Encoding.Unicode.GetBytes(text) : Encoding.Latin1.GetBytes(text));
        }

        return [.. result];
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTheStringDataOfAUnicodeShortcut()
    {
        byte[] link = Link(HasRelativePath | HasWorkingDir | HasArguments | HasIconLocation | IsUnicode,
            @"..\bin\example.exe", @"C:\Program Files\Example", "--verbose \"quoted\"", @"C:\icons\a.ico");

        List<string> lines = [.. ShellLinkReader.Read(link)];

        CollectionAssert.Contains(lines, @"Relative path: ..\bin\example.exe");
        CollectionAssert.Contains(lines, @"Working folder: C:\Program Files\Example");
        CollectionAssert.Contains(lines, "Arguments: --verbose \"quoted\"");
        CollectionAssert.Contains(lines, @"Icon: C:\icons\a.ico");
        Assert.IsTrue(lines.Any(l => l.Contains("It passes arguments", StringComparison.Ordinal)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsAnAnsiShortcutAndTheDescription()
    {
        byte[] link = Link(HasName | HasRelativePath, "My tool", @".\tool.exe");

        List<string> lines = [.. ShellLinkReader.Read(link)];

        CollectionAssert.Contains(lines, "Description: My tool");
        CollectionAssert.Contains(lines, @"Relative path: .\tool.exe");
        Assert.IsFalse(lines.Any(l => l.StartsWith("Hints", StringComparison.Ordinal)), "nothing to flag");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AShortcutToACommandInterpreterIsFlaggedAsAHintNotAVerdict()
    {
        byte[] link = Link(HasRelativePath | HasArguments | IsUnicode, @"..\..\Windows\System32\cmd.exe", "/c calc");

        List<string> lines = [.. ShellLinkReader.Read(link)];

        Assert.IsTrue(lines.Any(l => l.Contains("cmd.exe, which runs commands", StringComparison.Ordinal)), string.Join("\n", lines));
        CollectionAssert.Contains(lines, "Hints (not a verdict):");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ControlCharactersInTextAreDropped()
    {
        byte[] link = Link(HasArguments | IsUnicode, "a\u001b[2Jb");

        List<string> lines = [.. ShellLinkReader.Read(link)];

        CollectionAssert.Contains(lines, "Arguments: a[2Jb");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NotAShortcutIsSaidSo()
    {
        CollectionAssert.AreEqual(new[] { "[Not a Windows shortcut]" }, ShellLinkReader.Read(Encoding.ASCII.GetBytes("hello")).ToArray());
        CollectionAssert.AreEqual(new[] { "[Not a Windows shortcut]" }, ShellLinkReader.Read([]).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ATruncatedFileGivesANoticeAndNoException()
    {
        byte[] link = Link(HasRelativePath | IsUnicode, "abcdefgh");

        IReadOnlyList<string> lines = ShellLinkReader.Read(link[..^6]);

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the shortcut", StringComparison.Ordinal), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALinkInfoSizePastTheEndOfTheFileIsRefused()
    {
        byte[] link = [.. Link(HasLinkInfo), .. BitConverter.GetBytes(0x7FFFFFF0u)];

        IReadOnlyList<string> lines = ShellLinkReader.Read(link);

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the shortcut", StringComparison.Ordinal), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTheLocalPathOfTheLinkInfo()
    {
        // LinkInfo: size, header size 0x1C, flags 1 (local base path), volume id offset, base path offset, network offset, suffix offset.
        byte[] basePath = Encoding.Latin1.GetBytes("C:\\Windows\\notepad.exe\0");
        byte[] suffix = [0];
        int size = 0x1C + basePath.Length + suffix.Length;
        List<byte> info = [];
        info.AddRange(BitConverter.GetBytes((uint)size));
        info.AddRange(BitConverter.GetBytes(0x1Cu));
        info.AddRange(BitConverter.GetBytes(1u));
        info.AddRange(BitConverter.GetBytes(0u));
        info.AddRange(BitConverter.GetBytes(0x1Cu));
        info.AddRange(BitConverter.GetBytes(0u));
        info.AddRange(BitConverter.GetBytes((uint)(0x1C + basePath.Length)));
        info.AddRange(basePath);
        info.AddRange(suffix);
        byte[] link = [.. Link(HasLinkInfo), .. info];

        List<string> lines = [.. ShellLinkReader.Read(link)];

        CollectionAssert.Contains(lines, @"Target: C:\Windows\notepad.exe");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BidiOverridesAreShownAsReplacementCharacters()
    {
        byte[] link = Link(HasArguments | IsUnicode, "abc" + (char)0x202E + "def");

        List<string> lines = [.. ShellLinkReader.Read(link)];

        CollectionAssert.Contains(lines, "Arguments: abc" + (char)0xFFFD + "def");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALinkInfoOffsetOutsideItsStructureIsRefused()
    {
        List<byte> info = [];
        info.AddRange(BitConverter.GetBytes(0x20u));
        info.AddRange(BitConverter.GetBytes(0x1Cu));
        info.AddRange(BitConverter.GetBytes(1u));
        info.AddRange(BitConverter.GetBytes(0u));
        info.AddRange(BitConverter.GetBytes(0xFFFFFFF0u));
        info.AddRange(BitConverter.GetBytes(0u));
        info.AddRange(BitConverter.GetBytes(0u));
        info.AddRange(new byte[4]);
        byte[] link = [.. Link(HasLinkInfo), .. info];

        IReadOnlyList<string> lines = ShellLinkReader.Read(link);

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the shortcut", StringComparison.Ordinal), string.Join(Environment.NewLine, lines));
    }
}
