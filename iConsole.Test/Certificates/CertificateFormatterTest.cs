using iConsole.Certificates;

namespace iConsole.Test.Certificates;

[TestClass]
public class CertificateFormatterTest
{
    [TestMethod]
    public void Lines_ShowTheSubjectTheFingerprintsAndTheKeySize()
    {
        CertificateInfo info = CertificateReader.Read(CertificateTestData.SelfSignedRsa().RawData).Certificates.Single();

        IReadOnlyList<string> lines = CertificateFormatter.Lines(info);

        CollectionAssert.Contains(lines.ToList(), $"Subject: {info.Subject}");
        CollectionAssert.Contains(lines.ToList(), $"SHA-256: {info.Sha256Fingerprint}");
        Assert.IsTrue(lines.Any(line => line.StartsWith("Public key: RSA, 2048 bit", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validity_NamesTheStateAndItsDate()
    {
        CertificateInfo expired = CertificateReader.Read(CertificateTestData.SelfSignedRsa(daysBefore: 60, daysAfter: -1).RawData).Certificates.Single();

        StringAssert.StartsWith(CertificateFormatter.Validity(expired, CertificateTestData.Now), "expired on ");
    }
}
