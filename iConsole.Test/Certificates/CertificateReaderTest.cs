using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using iConsole.Certificates;

namespace iConsole.Test.Certificates;

[TestClass]
public class CertificateReaderTest
{
    private static CertificateReadResult ReadText(string text) =>
        CertificateReader.Read(Encoding.UTF8.GetBytes(text));

    [TestMethod]
    public void SinglePem_ReadsEveryFieldOfTheCertificate()
    {
        using var certificate = CertificateTestData.SelfSignedRsa();

        CertificateReadResult result = ReadText(CertificateTestData.Pem(certificate));

        Assert.IsNull(result.Error);
        CertificateInfo info = result.Certificates.Single();
        Assert.AreEqual("CN=example.test", info.Subject);
        Assert.AreEqual(info.Subject, info.Issuer, "a self-signed certificate is its own issuer");
        Assert.IsTrue(info.SelfIssued);
        Assert.AreEqual(certificate.SerialNumber, info.SerialNumber);
        Assert.AreEqual(2048, info.KeySize);
        Assert.AreEqual("RSA", info.PublicKeyAlgorithm);
        Assert.AreEqual(CertificateTestData.Now.AddDays(-30).ToUniversalTime().ToString("u"), info.NotBefore.ToUniversalTime().ToString("u"));
        CollectionAssert.AreEqual(new[] { "example.test", "127.0.0.1" }, info.SubjectAlternativeNames.ToArray());
        CollectionAssert.Contains(info.KeyUsages.ToList(), "DigitalSignature");
        CollectionAssert.Contains(info.EnhancedKeyUsages.ToList(), "TLS Web Server Authentication");
        Assert.IsFalse(info.IsCertificateAuthority);
        Assert.IsNull(info.PathLength);
    }

    [TestMethod]
    public void SinglePem_ShowsTheSha256AndSha1FingerprintsOfTheDer()
    {
        using var certificate = CertificateTestData.SelfSignedRsa();

        CertificateInfo info = ReadText(CertificateTestData.Pem(certificate)).Certificates.Single();

        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(certificate.RawData)), info.Sha256Fingerprint);
        Assert.AreEqual(Convert.ToHexString(SHA1.HashData(certificate.RawData)), info.Sha1Fingerprint);
    }

    [TestMethod]
    public void Bundle_ReadsEveryCertificateInTheOrderItWasWritten()
    {
        (X509Certificate2 authority, X509Certificate2 leaf) = CertificateTestData.AuthorityAndLeaf();
        using (authority)
        using (leaf)
        {
            CertificateReadResult result = ReadText(CertificateTestData.Pem(authority) + CertificateTestData.Pem(leaf));

            Assert.AreEqual(2, result.Certificates.Count);
            Assert.AreEqual("CN=Test Root", result.Certificates[0].Subject);
            Assert.AreEqual("CN=leaf.test", result.Certificates[1].Subject);
            Assert.AreEqual("CN=Test Root", result.Certificates[1].Issuer, "the leaf was signed by the authority");
            Assert.IsFalse(result.Certificates[1].SelfIssued);
            Assert.IsTrue(result.Certificates[0].IsCertificateAuthority, "the authority has the CA flag");
            Assert.AreEqual(1, result.Certificates[0].PathLength, "the authority's path length is the one it was given");
            Assert.AreEqual("RSA", result.Certificates[1].PublicKeyAlgorithm);
            Assert.AreEqual(2048, result.Certificates[1].KeySize);
        }
    }

    [TestMethod]
    public void Der_ReadsOneCertificate()
    {
        using var certificate = CertificateTestData.SelfSignedRsa("der.test");

        CertificateReadResult result = CertificateReader.Read(certificate.RawData);

        Assert.IsNull(result.Error);
        Assert.AreEqual("CN=der.test", result.Certificates.Single().Subject);
    }

    [TestMethod]
    public void Garbage_GivesNoCertificateAndAnErrorAndDoesNotThrow()
    {
        CertificateReadResult result = ReadText("this is not a certificate at all");

        Assert.AreEqual(0, result.Certificates.Count);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void EmptyInput_GivesNoCertificate()
    {
        CertificateReadResult result = CertificateReader.Read(ReadOnlyMemory<byte>.Empty);

        Assert.AreEqual(0, result.Certificates.Count);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void TruncatedPem_ReportsTheMissingEndLine()
    {
        using var certificate = CertificateTestData.SelfSignedRsa();
        string pem = CertificateTestData.Pem(certificate);
        string cut = pem[..pem.IndexOf("-----END CERTIFICATE-----", StringComparison.Ordinal)];

        CertificateReadResult result = ReadText(cut);

        Assert.AreEqual(0, result.Certificates.Count);
        StringAssert.Contains(result.Error, "no end line");
    }

    [TestMethod]
    public void BadBase64Block_IsSkippedAndTheGoodOneStillShows()
    {
        using var certificate = CertificateTestData.SelfSignedRsa();
        string bad = "-----BEGIN CERTIFICATE-----\n!!!!not-base64!!!!\n-----END CERTIFICATE-----\n";

        CertificateReadResult result = ReadText(bad + CertificateTestData.Pem(certificate));

        Assert.AreEqual(1, result.Certificates.Count, "the good certificate after the bad block is still read");
        Assert.AreEqual("CN=example.test", result.Certificates.Single().Subject);
        StringAssert.Contains(result.Error, "could not be read");
    }

    [TestMethod]
    public void MoreThanTheCap_ListsOnlyTheCap()
    {
        using var certificate = CertificateTestData.SelfSignedRsa();
        string one = CertificateTestData.Pem(certificate);
        string many = string.Concat(Enumerable.Repeat(one, CertificateLimits.MaxCertificates + 5));

        CertificateReadResult result = ReadText(many);

        Assert.AreEqual(CertificateLimits.MaxCertificates, result.Certificates.Count);
    }

    [TestMethod]
    public void PemWithKeyAndCertificate_CountsTheKeyAndNeverShowsItsMaterial()
    {
        using var certificate = CertificateTestData.SelfSignedRsa();
        string key = CertificateTestData.PrivateKeyPem();
        string keyBody = key.Split('\n')[1];

        CertificateReadResult result = ReadText(key + CertificateTestData.Pem(certificate));

        Assert.AreEqual(1, result.Certificates.Count);
        Assert.AreEqual(1, result.PrivateKeyBlocks);
        IEnumerable<string> shown = CertificateFormatter.Lines(result.Certificates.Single());
        Assert.IsFalse(shown.Any(line => line.Contains(keyBody, StringComparison.Ordinal)), "no line shows key material");
    }

    [TestMethod]
    public void PemWithOnlyAKey_HasNoCertificateButCountsTheKey()
    {
        CertificateReadResult result = ReadText(CertificateTestData.PrivateKeyPem());

        Assert.AreEqual(0, result.Certificates.Count);
        Assert.AreEqual(1, result.PrivateKeyBlocks);
    }

    [TestMethod]
    public void DerPrivateKey_HasNoCertificate()
    {
        using RSA key = RSA.Create(2048);
        byte[] der = key.ExportPkcs8PrivateKey();

        CertificateReadResult result = CertificateReader.Read(der);

        Assert.AreEqual(0, result.Certificates.Count);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void CertificateRequest_HasNoCertificate()
    {
        const string csr = "-----BEGIN CERTIFICATE REQUEST-----\nMIIBAAAA\n-----END CERTIFICATE REQUEST-----\n";

        CertificateReadResult result = ReadText(csr);

        Assert.AreEqual(0, result.Certificates.Count);
    }

    [TestMethod]
    public void ValidityAt_SaysValidExpiredOrNotYetValid()
    {
        using var valid = CertificateTestData.SelfSignedRsa(daysBefore: 30, daysAfter: 30);
        using var expired = CertificateTestData.SelfSignedRsa(daysBefore: 60, daysAfter: -1);
        using var future = CertificateTestData.SelfSignedRsa(daysBefore: -1, daysAfter: 60);

        CertificateInfo validInfo = CertificateReader.Read(valid.RawData).Certificates.Single();
        CertificateInfo expiredInfo = CertificateReader.Read(expired.RawData).Certificates.Single();
        CertificateInfo futureInfo = CertificateReader.Read(future.RawData).Certificates.Single();

        Assert.AreEqual(CertificateValidity.Valid, validInfo.ValidityAt(CertificateTestData.Now));
        Assert.AreEqual(CertificateValidity.Expired, expiredInfo.ValidityAt(CertificateTestData.Now));
        Assert.AreEqual(CertificateValidity.NotYetValid, futureInfo.ValidityAt(CertificateTestData.Now));
    }

    [TestMethod]
    public void Printable_ReplacesControlCharactersSoTheyCannotMoveTheCursor()
    {
        Assert.AreEqual("a?b?c", CertificateReader.Printable("a\u001bb\u0007c"));
    }

    [TestMethod]
    public void Printable_ReplacesFormatCharactersThatReorderText()
    {
        Assert.AreEqual("a?b", CertificateReader.Printable("a\u202Eb"), "a right-to-left override reverses what follows it");
        Assert.AreEqual("a?b", CertificateReader.Printable("a\u200Db"), "a zero-width joiner is hidden text");
    }
}
