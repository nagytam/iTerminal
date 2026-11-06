using iConsole.Hdf;

namespace iConsole.Test.Hdf;

[TestClass]
public class HdfFileTypesTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iconsole-hdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("a.h5", true)]
    [DataRow("A.HDF5", true)]
    [DataRow("scan.hdf", true)]
    [DataRow("swath.he5", true)]
    [DataRow("a.h5.txt", false)]
    [DataRow("a.h4", false)]
    [DataRow("notes.txt", false)]
    [DataRow("", false)]
    public void TheEndingsTheViewerOpens(string path, bool expected)
    {
        Assert.AreEqual(expected, HdfFileTypes.IsHdfFile(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSignatureAtTheStartOfTheFileCounts()
    {
        string path = Write("a.h5", [.. HdfFileTypes.Signature, 0, 0, 0, 0]);

        Assert.IsTrue(HdfFileTypes.FileHasSignature(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(512)]
    [DataRow(1024)]
    [DataRow(4096)]
    public void TheSignatureAfterAUserBlockCounts(int offset)
    {
        byte[] content = new byte[offset + 64];
        HdfFileTypes.Signature.CopyTo(content, offset);

        Assert.IsTrue(HdfFileTypes.FileHasSignature(Write("blocked.h5", content)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSignatureAtAnOffsetTheFormatDoesNotAllowDoesNotCount()
    {
        byte[] content = new byte[300];
        HdfFileTypes.Signature.CopyTo(content, 100);

        Assert.IsFalse(HdfFileTypes.FileHasSignature(Write("odd.h5", content)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyFileAndAShortFileAreNotHdf5()
    {
        Assert.IsFalse(HdfFileTypes.FileHasSignature(Write("empty.h5", [])));
        Assert.IsFalse(HdfFileTypes.FileHasSignature(Write("short.h5", HdfFileTypes.Signature[..4])));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnHdf4FileWithTheSameEndingIsNotHdf5()
    {
        // HDF4 files begin with 0x0E 0x03 0x13 0x01.
        string path = Write("old.hdf", [0x0E, 0x03, 0x13, 0x01, 0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.IsTrue(HdfFileTypes.IsHdfFile(path));
        Assert.IsFalse(HdfFileTypes.FileHasSignature(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasSignatureReadsOnlyTheBytesItIsGiven()
    {
        Assert.IsTrue(HdfFileTypes.HasSignature(HdfFileTypes.Signature));
        Assert.IsFalse(HdfFileTypes.HasSignature(HdfFileTypes.Signature.AsSpan(0, 7)));
        Assert.IsFalse(HdfFileTypes.HasSignature("SQLite format 3\0"u8));
    }
}
