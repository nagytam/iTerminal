using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Services.Properties;
using iTerminal.Core.Test.Services.Links;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace iTerminal.Core.Test.Services.Checksums;

[TestClass]
public class ChecksumAndPropertiesTest
{
    // The checksums of the three bytes "abc", from the algorithms' own specifications.
    private const string AbcMd5 = "900150983cd24fb0d6963f7d28e17f72";
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private string _root = null!;

    [TestInitialize]
    public void CreateSandbox() => _root = Directory.CreateTempSubdirectory("iTerminal.Checksums.").FullName;

    [TestCleanup]
    public void DeleteSandbox()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(content));
        return path;
    }

    #region Computing
    [TestMethod]
    public async Task ComputeAsync_ALocalFile_GivesEveryAlgorithmInOnePass()
    {
        var path = CreateFile("abc.txt", "abc");
        var reported = new List<long>();

        var result = await new ChecksumService().ComputeAsync(new Url("file://" + path), null!, new Recorder(reported));

        Assert.AreEqual(3, result.Bytes);
        Assert.AreEqual(AbcMd5, result.Hashes[ChecksumAlgorithm.Md5]);
        Assert.AreEqual(AbcSha1, result.Hashes[ChecksumAlgorithm.Sha1]);
        Assert.AreEqual(AbcSha256, result.Hashes[ChecksumAlgorithm.Sha256]);
        Assert.AreEqual(Convert.ToHexStringLower(SHA512.HashData("abc"u8)), result.Hashes[ChecksumAlgorithm.Sha512]);
        Assert.AreEqual(3, reported.Last());
    }

    private sealed class Recorder(List<long> reported) : IProgress<long>
    {
        public void Report(long value) => reported.Add(value);
    }

    [TestMethod]
    public async Task ComputeAsync_AFileOnAnotherDrive_IsStreamedThroughItsFileSystem()
    {
        var url = new Url("memdrive://abc.txt");
        var fileSystem = new Mock<IFileSystem>();
        fileSystem
            .Setup(f => f.StreamToAsync(url, It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .Returns<Url, Stream, IProgress<long>?, CancellationToken>(async (_, destination, _, token) =>
            {
                // In two pieces, as a network stream would deliver it.
                await destination.WriteAsync("ab"u8.ToArray(), token);
                await destination.WriteAsync("c"u8.ToArray(), token);
            });

        var result = await new ChecksumService().ComputeAsync(url, fileSystem.Object);

        Assert.AreEqual(AbcSha256, result.Hashes[ChecksumAlgorithm.Sha256]);
        Assert.AreEqual(3, result.Bytes);
    }

    [TestMethod]
    public async Task ComputeAsync_AnEmptyFile_HasChecksumsToo()
    {
        var path = CreateFile("empty.bin", string.Empty);

        var result = await new ChecksumService().ComputeAsync(new Url("file://" + path), null!);

        Assert.AreEqual("d41d8cd98f00b204e9800998ecf8427e", result.Hashes[ChecksumAlgorithm.Md5]);
        Assert.AreEqual(0, result.Bytes);
    }

    [TestMethod]
    public async Task ComputeAsync_Cancelled_Stops()
    {
        var path = CreateFile("abc.txt", "abc");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new ChecksumService().ComputeAsync(new Url("file://" + path), null!, null, cancellation.Token));
    }
    #endregion

    #region Reading a checksum someone wrote down
    [TestMethod]
    public void AlgorithmFor_GoesByLength_AndRefusesWhatIsNotHex()
    {
        Assert.AreEqual(ChecksumAlgorithm.Md5, ChecksumService.AlgorithmFor(AbcMd5));
        Assert.AreEqual(ChecksumAlgorithm.Sha1, ChecksumService.AlgorithmFor(AbcSha1));
        Assert.AreEqual(ChecksumAlgorithm.Sha256, ChecksumService.AlgorithmFor(AbcSha256.ToUpperInvariant()));
        Assert.AreEqual(ChecksumAlgorithm.Sha512, ChecksumService.AlgorithmFor(new string('a', 128)));
        Assert.AreEqual(ChecksumAlgorithm.Md5, ChecksumService.AlgorithmFor("90:01:50:98:3c:d2:4f:b0 d6-96-3f-7d-28-e1-7f-72"), "as some tools print it");
        Assert.IsNull(ChecksumService.AlgorithmFor(AbcMd5[..31]));
        Assert.IsNull(ChecksumService.AlgorithmFor(new string('z', 32)));
        Assert.IsNull(ChecksumService.AlgorithmFor(null));
    }

    [TestMethod]
    public void ParseChecksumFile_ReadsWhatTheCommonToolsWrite()
    {
        Assert.AreEqual(AbcSha256, ChecksumService.ParseChecksumFile($"{AbcSha256}  abc.txt\n", "abc.txt"), "sha256sum, text mode");
        Assert.AreEqual(AbcSha256, ChecksumService.ParseChecksumFile($"{AbcSha256.ToUpperInvariant()} *abc.txt\r\n", "ABC.TXT"), "binary mode, another case");
        Assert.AreEqual(AbcSha256, ChecksumService.ParseChecksumFile($"SHA256 (abc.txt) = {AbcSha256}", "abc.txt"), "BSD and openssl");
        Assert.AreEqual(AbcMd5, ChecksumService.ParseChecksumFile($"﻿{AbcMd5}\n", "abc.txt"), "nothing but the hash");
        Assert.AreEqual(AbcSha256, ChecksumService.ParseChecksumFile($"{AbcSha256}  ./dist/abc.txt", "abc.txt"), "listed with its folder");
    }

    [TestMethod]
    public void ParseChecksumFile_InAList_TakesThisFilesLine_AndNoOther()
    {
        var list = $"# release 1.2\n{new string('1', 64)}  other.zip\n{AbcSha256}  abc.txt\n{new string('2', 64)}  abc.txt.sig\n";

        Assert.AreEqual(AbcSha256, ChecksumService.ParseChecksumFile(list, "abc.txt"));
        Assert.IsNull(ChecksumService.ParseChecksumFile(list, "missing.txt"));
        Assert.IsNull(ChecksumService.ParseChecksumFile($"{AbcMd5}\n{AbcSha1}\n", "abc.txt"), "two bare hashes are a list of somebody else's files");
        Assert.IsNull(ChecksumService.ParseChecksumFile("not a checksum file", "abc.txt"));
    }

    [TestMethod]
    public void FindWrittenChecksum_LooksBesideTheFile_AndInTheFoldersList()
    {
        var path = CreateFile("abc.txt", "abc");
        Assert.IsNull(ChecksumService.FindWrittenChecksum(path));

        CreateFile("SHA256SUMS", $"{new string('1', 64)}  other.zip\n{AbcSha256}  abc.txt\n");
        var fromList = ChecksumService.FindWrittenChecksum(path);
        Assert.AreEqual(new ExpectedChecksum(ChecksumAlgorithm.Sha256, AbcSha256, "SHA256SUMS"), fromList);

        // The file's own checksum file is asked before the folder's list.
        CreateFile("abc.txt.md5", AbcMd5);
        Assert.AreEqual(new ExpectedChecksum(ChecksumAlgorithm.Md5, AbcMd5, "abc.txt.md5"), ChecksumService.FindWrittenChecksum(path));
    }

    [TestMethod]
    public void CompareVerdict_SaysWhichAlgorithmMatched_OrThatItIsNotAChecksumYet()
    {
        var result = new ChecksumResult(new Dictionary<ChecksumAlgorithm, string>
        {
            [ChecksumAlgorithm.Md5] = AbcMd5,
            [ChecksumAlgorithm.Sha1] = AbcSha1,
            [ChecksumAlgorithm.Sha256] = AbcSha256,
            [ChecksumAlgorithm.Sha512] = new string('a', 128),
        }, 3);

        Assert.IsNull(ChecksumDialog.CompareVerdict("  ", result));
        Assert.AreEqual(("✓ Matches the SHA-256.", (bool?)true), ChecksumDialog.CompareVerdict(" " + AbcSha256.ToUpperInvariant() + " ", result));
        Assert.AreEqual(("✗ Does NOT match the MD5.", (bool?)false), ChecksumDialog.CompareVerdict(new string('0', 32), result));
        Assert.IsNull(ChecksumDialog.CompareVerdict(AbcMd5[..10], result)!.Value.Matches);
        StringAssert.Contains(ChecksumDialog.CompareVerdict(AbcMd5[..10], result)!.Value.Text, "10 characters");
        StringAssert.Contains(ChecksumDialog.CompareVerdict(AbcSha1, null)!.Value.Text, "as soon as the file is read");
    }
    #endregion

    #region Properties
    [TestMethod]
    public void Read_AFile_AndAFolder()
    {
        var path = CreateFile(Path.Combine("data", "abc.txt"), "abc");
        var service = new FilePropertiesService();

        var file = service.Read(path);
        var folder = service.Read(Path.Combine(_root, "data") + Path.DirectorySeparatorChar);

        Assert.AreEqual("abc.txt", file.Name);
        Assert.IsFalse(file.IsFolder);
        Assert.AreEqual(3, file.Size);
        Assert.IsNull(file.LinkTarget);
        Assert.IsTrue((DateTime.Now - file.Modified).Duration() < TimeSpan.FromMinutes(5));

        Assert.AreEqual("data", folder.Name);
        Assert.IsTrue(folder.IsFolder);
        Assert.IsNull(folder.Size, "a folder's size has to be added up");

        Assert.ThrowsExactly<IOException>(() => service.Read(Path.Combine(_root, "missing")));
    }

    [TestMethod]
    public async Task MeasureAsync_AddsUpEverythingUnderTheFolder_ButNotThroughALink()
    {
        CreateFile(Path.Combine("data", "a.txt"), "12345");
        CreateFile(Path.Combine("data", "sub", "b.txt"), "123");
        CreateFile(Path.Combine("outside", "huge.bin"), new string('x', 10_000));
        LinkServiceTest.CreateFolderLink(Path.Combine(_root, "data", "link"), Path.Combine(_root, "outside"));

        var measure = await new FilePropertiesService().MeasureAsync(Path.Combine(_root, "data"));

        Assert.AreEqual(2, measure.Files);
        Assert.AreEqual(2, measure.Folders, "sub, and the link — counted as an entry, not entered");
        Assert.AreEqual(8, measure.Bytes);
        Assert.AreEqual(0, measure.Unreadable);
    }

    [TestMethod]
    public void Read_ALink_IsTheLinkItself()
    {
        Directory.CreateDirectory(Path.Combine(_root, "target"));
        LinkServiceTest.CreateFolderLink(Path.Combine(_root, "link"), Path.Combine(_root, "target"));

        var properties = new FilePropertiesService().Read(Path.Combine(_root, "link"));

        Assert.IsTrue(properties.IsFolder);
        Assert.IsNotNull(properties.LinkTarget);
        StringAssert.EndsWith(Path.TrimEndingDirectorySeparator(properties.LinkTarget), "target");
    }

    [TestMethod]
    public void Apply_ChangesWhatTheUserSets_AndLeavesTheRest()
    {
        var path = CreateFile("abc.txt", "abc");
        var service = new FilePropertiesService();

        if (OperatingSystem.IsWindows())
        {
            service.Apply(path, FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Directory, unixMode: null);

            var attributes = File.GetAttributes(path);
            Assert.IsTrue(attributes.HasFlag(FileAttributes.ReadOnly));
            Assert.IsTrue(attributes.HasFlag(FileAttributes.Hidden));
            Assert.IsFalse(attributes.HasFlag(FileAttributes.Archive), "unticked");
            Assert.IsFalse(attributes.HasFlag(FileAttributes.Directory), "a fact about the entry, not a setting");

            service.Apply(path, FileAttributes.Archive, unixMode: null);
            Assert.IsFalse(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        }
        else
        {
            service.Apply(path, attributes: null, FilePropertiesService.ParseOctal("640"));

            Assert.AreEqual("640", FilePropertiesService.ToOctal(File.GetUnixFileMode(path)));
        }
    }

    [TestMethod]
    public void PermissionBits_AsOctalAndAsLetters()
    {
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        Assert.AreEqual("755", FilePropertiesService.ToOctal(mode));
        Assert.AreEqual("rwxr-xr-x", FilePropertiesService.ToSymbolic(mode));
        Assert.AreEqual(mode, FilePropertiesService.ParseOctal(" 755 "));
        Assert.AreEqual("4755", FilePropertiesService.ToOctal(mode | UnixFileMode.SetUser));
        Assert.AreEqual("000", FilePropertiesService.ToOctal(UnixFileMode.None));
        Assert.IsNull(FilePropertiesService.ParseOctal("75"));
        Assert.IsNull(FilePropertiesService.ParseOctal("789"));
        Assert.IsNull(FilePropertiesService.ParseOctal("rwx"));
    }

    [TestMethod]
    public void FormatSize_IsForReadingAndForChecking()
    {
        StringAssert.EndsWith(FilePropertiesService.FormatSize(1_468_006), "(1,468,006 bytes)");
        StringAssert.Contains(FilePropertiesService.FormatSize(1_468_006), "MB");
    }
    #endregion
}
