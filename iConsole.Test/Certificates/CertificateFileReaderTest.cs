using iConsole.Certificates;

namespace iConsole.Test.Certificates;

[TestClass]
public class CertificateFileReaderTest
{
    private string _path = string.Empty;

    [TestInitialize]
    public void Setup() => _path = Path.Combine(Path.GetTempPath(), "cert-reader-" + Guid.NewGuid().ToString("N") + ".pem");

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [TestMethod]
    public void SmallFile_IsReadWhole()
    {
        File.WriteAllText(_path, "abc");

        CertificateFileRead read = CertificateFileReader.ReadCapped(_path);

        Assert.AreEqual(CertificateFileStatus.Ok, read.Status);
        CollectionAssert.AreEqual("abc"u8.ToArray(), read.Bytes);
    }

    [TestMethod]
    public void ExactlyTheCap_IsStillRead()
    {
        File.WriteAllBytes(_path, new byte[CertificateLimits.MaxFileBytes]);

        CertificateFileRead read = CertificateFileReader.ReadCapped(_path);

        Assert.AreEqual(CertificateFileStatus.Ok, read.Status);
        Assert.AreEqual(CertificateLimits.MaxFileBytes, read.Bytes.Length);
    }

    [TestMethod]
    public void OneByteOverTheCap_IsAnOverflowWithNoBytesKept()
    {
        File.WriteAllBytes(_path, new byte[CertificateLimits.MaxFileBytes + 1]);

        CertificateFileRead read = CertificateFileReader.ReadCapped(_path);

        Assert.AreEqual(CertificateFileStatus.Overflow, read.Status);
        Assert.AreEqual(0, read.Bytes.Length);
    }

    [TestMethod]
    public void MissingFile_IsAFailureNotAnException()
    {
        CertificateFileRead read = CertificateFileReader.ReadCapped(_path);

        Assert.AreEqual(CertificateFileStatus.Failure, read.Status);
        Assert.IsNotNull(read.Error);
    }
}
