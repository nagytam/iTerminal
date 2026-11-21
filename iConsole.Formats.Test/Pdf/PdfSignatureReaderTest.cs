using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using iConsole.Formats.Pdf;

namespace iConsole.Formats.Test.Pdf;

[TestClass]
public class PdfSignatureReaderTest
{
    private const int HexLength = 8192;

    /// <summary>A signing certificate with its key, valid around the real clock (the chain check uses it).</summary>
    private static X509Certificate2 Certificate(string subject = "CN=Jane Doe", int daysBefore = 1, int daysAfter = 30)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-daysBefore), DateTimeOffset.UtcNow.AddDays(daysAfter));
        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx, "x"), "x");
    }

    /// <summary>A root and a leaf it signs; the leaf's validity is given, the root's covers it.</summary>
    private static (X509Certificate2 Root, X509Certificate2 Leaf) RootAndLeaf(int leafDaysBefore, int leafDaysAfter)
    {
        using RSA rootKey = RSA.Create(2048);
        CertificateRequest rootRequest = new("CN=Pdf Test Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        X509Certificate2 root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-400), DateTimeOffset.UtcNow.AddDays(400));

        using RSA leafKey = RSA.Create(2048);
        CertificateRequest leafRequest = new("CN=Signer Leaf", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        using X509Certificate2 unsigned = leafRequest.Create(
            root, DateTimeOffset.UtcNow.AddDays(-leafDaysBefore), DateTimeOffset.UtcNow.AddDays(leafDaysAfter), RandomNumberGenerator.GetBytes(8));
        using X509Certificate2 withKey = unsigned.CopyWithPrivateKey(leafKey);
        return (root, X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx, "x"), "x"));
    }

    /// <summary>
    /// A minimal PDF whose signature dictionary carries a real CMS signature over everything but its /Contents string. The numbers
    /// in /ByteRange are fixed width so they can be filled in after the layout is known.
    /// </summary>
    private static byte[] SignedPdf(
        X509Certificate2 signer,
        string subFilter = "adbe.pkcs7.detached",
        string name = "Jane Doe",
        string trailing = "",
        bool withSigningTime = true)
    {
        string head = "%PDF-1.7\n1 0 obj\n<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /" + subFilter + " /Name (" + name + ") /ByteRange [0 0000000000 0000000000 0000000000] /Contents ";
        string tail = "\n/M (D:20260601120000Z) >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n" + trailing;
        byte[] headBytes = Encoding.Latin1.GetBytes(head);
        byte[] tailBytes = Encoding.Latin1.GetBytes(tail);

        long second = headBytes.Length;
        long third = headBytes.Length + 1 + HexLength + 1;
        string numbers = string.Create(CultureInfo.InvariantCulture, $"[0 {second:D10} {third:D10} {tailBytes.Length:D10}]");
        headBytes = Encoding.Latin1.GetBytes(head.Replace("[0 0000000000 0000000000 0000000000]", numbers));

        byte[] signed = [.. headBytes, .. tailBytes];
        byte[] cms = Sign(signer, signed, subFilter, withSigningTime);
        string hex = Convert.ToHexString(cms).PadRight(HexLength, '0');
        Assert.AreEqual(HexLength, hex.Length, "The placeholder is too small for the signature.");
        return [.. headBytes, .. Encoding.ASCII.GetBytes("<" + hex + ">"), .. tailBytes];
    }

    private static byte[] Sign(X509Certificate2 signer, byte[] signed, string subFilter, bool withSigningTime)
    {
        CmsSigner cmsSigner = new(signer) { IncludeOption = X509IncludeOption.EndCertOnly };
        if (withSigningTime)
        {
            cmsSigner.SignedAttributes.Add(new Pkcs9SigningTime(new DateTime(2026, 6, 1, 12, 30, 15, DateTimeKind.Utc)));
        }

        if (subFilter == "adbe.pkcs7.sha1")
        {
            SignedCms sha1Cms = new(new ContentInfo(SHA1.HashData(signed)), detached: false);
            sha1Cms.ComputeSignature(cmsSigner);
            return sha1Cms.Encode();
        }

        SignedCms cms = new(new ContentInfo(signed), detached: true);
        cms.ComputeSignature(cmsSigner);
        return cms.Encode();
    }

    private static PdfSignature Only(PdfSignatureReport report)
    {
        Assert.AreEqual(1, report.Signatures.Count);
        return report.Signatures[0];
    }

    private static int IndexOf(byte[] data, string text) => data.AsSpan().IndexOf(Encoding.Latin1.GetBytes(text));

    [Timeout(60_000)]
    [TestMethod]
    public void AValidSelfSignedPdf_IsIntactButTheSignerIsNotTrusted()
    {
        using X509Certificate2 certificate = Certificate();

        PdfSignature signature = Only(PdfSignatureReader.Read(SignedPdf(certificate)));

        Assert.AreEqual(PdfSignatureIntegrity.Valid, signature.Integrity);
        Assert.IsTrue(signature.CoversWholeFile);
        Assert.AreEqual("adbe.pkcs7.detached", signature.SubFilter);
        Assert.AreEqual("Jane Doe", signature.Name);
        Assert.AreEqual("CN=Jane Doe", signature.Signer);
        // Whatever the platform's trust store holds, a fresh self-signed certificate is not in it.
        Assert.AreEqual(PdfSignerTrust.NotTrusted, signature.Trust);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AByteChangedInsideASignedRange_IsInvalid()
    {
        using X509Certificate2 certificate = Certificate();
        byte[] pdf = SignedPdf(certificate);
        pdf[IndexOf(pdf, "1.7")] = (byte)'9';

        PdfSignature signature = Only(PdfSignatureReader.Read(pdf));

        Assert.AreEqual(PdfSignatureIntegrity.Invalid, signature.Integrity);
        Assert.AreEqual(PdfSignerTrust.Unknown, signature.Trust, "A changed file gives no trust verdict.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AByteChangedInTheSignaturePadding_LeavesTheSignatureValid()
    {
        // The hex string is outside the signed ranges, so only the ranges decide: the padding after the structure is ignored.
        using X509Certificate2 certificate = Certificate();
        byte[] pdf = SignedPdf(certificate);
        int contentsEnd = IndexOf(pdf, ">\n/M (D:");
        pdf[contentsEnd - 1] = (byte)'1';

        Assert.AreEqual(PdfSignatureIntegrity.Valid, Only(PdfSignatureReader.Read(pdf)).Integrity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARevisionAppendedAfterSigning_IsValidButNotTheWholeFile()
    {
        using X509Certificate2 certificate = Certificate();
        // The signed ranges end where the signed text ends, so text after the original %%EOF is outside them.
        byte[] original = SignedPdf(certificate);
        byte[] appended = [.. original, .. Encoding.Latin1.GetBytes("2 0 obj\n<< /Added true >>\nendobj\n%%EOF\n")];
        PdfSignature signature = Only(PdfSignatureReader.Read(appended));

        Assert.AreEqual(PdfSignatureIntegrity.Valid, signature.Integrity);
        Assert.IsFalse(signature.CoversWholeFile);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TrailingWhitespaceAfterTheSignedRange_StillCountsAsTheWholeFile()
    {
        using X509Certificate2 certificate = Certificate();
        byte[] padded = [.. SignedPdf(certificate), .. "\r\n\r\n"u8.ToArray()];

        Assert.IsTrue(Only(PdfSignatureReader.Read(padded)).CoversWholeFile);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASignerChainingToAConfiguredRoot_IsTrusted()
    {
        (X509Certificate2 root, X509Certificate2 leaf) = RootAndLeaf(leafDaysBefore: 1, leafDaysAfter: 30);
        using (root)
        using (leaf)
        {
            PdfSignature signature = Only(PdfSignatureReader.Read(SignedPdf(leaf), policy =>
            {
                policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                policy.CustomTrustStore.Add(root);
            }));

            Assert.AreEqual(PdfSignatureIntegrity.Valid, signature.Integrity);
            Assert.AreEqual(PdfSignerTrust.Trusted, signature.Trust);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnExpiredSignerCertificate_IsReportedAsExpired()
    {
        (X509Certificate2 root, X509Certificate2 leaf) = RootAndLeaf(leafDaysBefore: 60, leafDaysAfter: -10);
        using (root)
        using (leaf)
        {
            PdfSignature signature = Only(PdfSignatureReader.Read(SignedPdf(leaf), policy =>
            {
                policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                policy.CustomTrustStore.Add(root);
            }));

            Assert.AreEqual(PdfSignatureIntegrity.Valid, signature.Integrity, "An expired certificate still signed these bytes.");
            Assert.AreEqual(PdfSignerTrust.Expired, signature.Trust);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheChainIsBuiltWithoutTheNetwork()
    {
        using X509Certificate2 certificate = Certificate();
        X509ChainPolicy? seen = null;

        PdfSignatureReader.Read(SignedPdf(certificate), policy => seen = policy);

        Assert.IsNotNull(seen);
        Assert.AreEqual(X509RevocationMode.NoCheck, seen.RevocationMode);
        Assert.IsTrue(seen.DisableCertificateDownloads);
        Assert.IsTrue(seen.UrlRetrievalTimeout <= TimeSpan.FromSeconds(5));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASha1FlavouredSignature_IsChecked()
    {
        using X509Certificate2 certificate = Certificate();

        PdfSignature signature = Only(PdfSignatureReader.Read(SignedPdf(certificate, "adbe.pkcs7.sha1")));

        Assert.AreEqual(PdfSignatureIntegrity.Valid, signature.Integrity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASha1FlavouredSignature_OfChangedBytes_IsInvalid()
    {
        using X509Certificate2 certificate = Certificate();
        byte[] pdf = SignedPdf(certificate, "adbe.pkcs7.sha1");
        pdf[IndexOf(pdf, "1.7")] = (byte)'9';

        Assert.AreEqual(PdfSignatureIntegrity.Invalid, Only(PdfSignatureReader.Read(pdf)).Integrity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnsupportedSubFilter_IsNotCalledInvalid()
    {
        using X509Certificate2 certificate = Certificate();

        PdfSignature signature = Only(PdfSignatureReader.Read(SignedPdf(certificate, "adbe.x509.rsa_sha1")));

        Assert.AreEqual(PdfSignatureIntegrity.Unsupported, signature.Integrity);
        Assert.AreEqual(PdfSignerTrust.Unknown, signature.Trust);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APdfWithoutSignatures_HasNoReportAndNoSection()
    {
        byte[] pdf = Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF\n");

        PdfSignatureReport report = PdfSignatureReader.Read(pdf);

        Assert.AreEqual(0, report.Signatures.Count);
        Assert.AreEqual(0, PdfSignatureReader.Describe(report).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("[0 0000000010 0000000020 9999999999]", DisplayName = "Range past the end of the file")]
    [DataRow("[5 0000000010 0000000020 0000000010]", DisplayName = "Range that does not start the file")]
    [DataRow("[0 0000000020 0000000010 0000000010]", DisplayName = "Ranges that overlap")]
    [DataRow("[0 0000000010 0000000012 0000000010]", DisplayName = "Gap too small for the contents")]
    public void ARangeThatDoesNotBracketTheContents_IsUnreadableNotACrash(string range)
    {
        using X509Certificate2 certificate = Certificate();
        string text = Encoding.Latin1.GetString(SignedPdf(certificate));
        byte[] pdf = Encoding.Latin1.GetBytes(text.Replace(RangeIn(text), range));

        PdfSignature signature = Only(PdfSignatureReader.Read(pdf));

        Assert.AreEqual(PdfSignatureIntegrity.Unreadable, signature.Integrity);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("[0 -000000010 0000000020 0000000010]", DisplayName = "Negative number")]
    [DataRow("[0 99999999999999 0000000020 0000000010]", DisplayName = "Number too long to be a file offset")]
    [DataRow("[0 0000000010 0000000020]", DisplayName = "Three numbers")]
    public void ARangeThatIsNotFourOffsets_IsIgnored(string range)
    {
        using X509Certificate2 certificate = Certificate();
        string text = Encoding.Latin1.GetString(SignedPdf(certificate));
        byte[] pdf = Encoding.Latin1.GetBytes(text.Replace(RangeIn(text), range));

        Assert.AreEqual(0, PdfSignatureReader.Read(pdf).Signatures.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContentsThatAreNotHex_AreUnreadable()
    {
        using X509Certificate2 certificate = Certificate();
        byte[] pdf = SignedPdf(certificate);
        pdf[IndexOf(pdf, "<30") + 5] = (byte)'Z';

        Assert.AreEqual(PdfSignatureIntegrity.Unreadable, Only(PdfSignatureReader.Read(pdf)).Integrity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContentsThatAreNotACmsStructure_AreUnreadable()
    {
        using X509Certificate2 certificate = Certificate();
        byte[] pdf = SignedPdf(certificate);
        int start = IndexOf(pdf, "<30") + 1;
        Encoding.ASCII.GetBytes("3003020100").CopyTo(pdf, start);

        Assert.AreEqual(PdfSignatureIntegrity.Unreadable, Only(PdfSignatureReader.Read(pdf)).Integrity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoreThanTheLimit_AreCountedNotShown()
    {
        string text = "%PDF-1.7\n" + string.Concat(Enumerable.Repeat("<< /ByteRange [0 1 2 3] >>\n", 40));

        PdfSignatureReport report = PdfSignatureReader.Read(Encoding.Latin1.GetBytes(text));

        Assert.AreEqual(PdfSignatureReader.MaxSignatures, report.Signatures.Count);
        Assert.AreEqual(8, report.NotShown);
        Assert.IsTrue(PdfSignatureReader.Describe(report).Any(l => l.Contains("8 more signatures")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFileOverTheSizeLimit_IsNotRead()
    {
        byte[] huge = new byte[PdfSignatureReader.MaxBytes + 1];

        PdfSignatureReport report = PdfSignatureReader.Read(huge);

        Assert.IsTrue(report.TooLarge);
        Assert.IsTrue(PdfSignatureReader.Describe(report).Any(l => l.Contains("larger than 64 MB")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextFromTheFile_LosesEscapeSequencesAndBidiOverridesAndIsShortened()
    {
        Assert.AreEqual("[31mred", PdfSignatureReader.Clean("\u001b[31mred"));
        Assert.AreEqual("Jane Doe", PdfSignatureReader.Clean("Jane‮ Doe"));
        Assert.AreEqual("ab", PdfSignatureReader.Clean("a\u0085b"));
        string cut = PdfSignatureReader.Clean(new string('x', 500));
        Assert.IsTrue(cut.Length <= 201 && cut.EndsWith('…'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEscapeSequenceInTheSignerName_NeverReachesTheLines()
    {
        using X509Certificate2 certificate = Certificate();
        byte[] pdf = SignedPdf(certificate, name: "Jane\u001b[2J Doe");

        string[] lines = [.. PdfSignatureReader.Describe(PdfSignatureReader.Read(pdf))];

        Assert.IsTrue(lines.Length > 0);
        Assert.IsFalse(lines.Any(l => l.Contains('\u001b')), string.Join('\n', lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSection_SaysIntegrityAndTrustSeparatelyAndThatRevocationIsNotChecked()
    {
        using X509Certificate2 certificate = Certificate();

        string[] lines = [.. PdfSignatureReader.Describe(PdfSignatureReader.Read(SignedPdf(certificate)))];

        Assert.IsTrue(lines[0].Contains("Digital signatures"), lines[0]);
        Assert.IsTrue(lines.Any(l => l.Contains("The signed bytes are unchanged.")));
        Assert.IsTrue(lines.Any(l => l.Contains("The whole file.")));
        Assert.IsTrue(lines.Any(l => l.Contains("Not trusted:")));
        Assert.IsFalse(lines.Any(l => l.Contains("Trusted:")));
        Assert.IsTrue(lines.Any(l => l.Contains("Revocation was not checked.")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TwoSignatures_AreNumberedAndEachChecked()
    {
        using X509Certificate2 certificate = Certificate();
        byte[] first = SignedPdf(certificate);
        byte[] second = SignedPdf(certificate, name: "Second");
        byte[] both = [.. first, .. second];

        PdfSignatureReport report = PdfSignatureReader.Read(both);

        // Both dictionaries are found and numbered in file order; the first one is checked against its own bytes.
        Assert.AreEqual(2, report.Signatures.Count);
        Assert.AreEqual(PdfSignatureIntegrity.Valid, report.Signatures[0].Integrity);
        CollectionAssert.AreEqual(new[] { 1, 2 }, report.Signatures.Select(s => s.Number).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AContentsStringFarLargerThanASignature_IsNotDecoded()
    {
        // 600 KB of hex between two ranges that otherwise bracket it correctly.
        string hex = new('0', 600 * 1024);
        string head = "%PDF-1.7\n1 0 obj\n<< /SubFilter /adbe.pkcs7.detached /ByteRange [0 0000000044 0000614446 0000000010] /Contents ";
        string tail = "\n>>\nendobj\n";
        byte[] pdf = Encoding.Latin1.GetBytes(head + "<" + hex + ">" + tail);
        string text = Encoding.Latin1.GetString(pdf);
        int b = head.Length;
        int c = head.Length + 1 + hex.Length + 1;
        pdf = Encoding.Latin1.GetBytes(text.Replace("[0 0000000044 0000614446 0000000010]", $"[0 {b:D10} {c:D10} {tail.Length:D10}]"));

        Assert.AreEqual(PdfSignatureIntegrity.Unreadable, Only(PdfSignatureReader.Read(pdf)).Integrity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ACancelledRead_StopsInsteadOfFinishingTheChecks()
    {
        using X509Certificate2 certificate = Certificate();
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => PdfSignatureReader.Read(SignedPdf(certificate), null, cancelled.Token));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheEntriesOfANeighbouringObject_AreNotTakenForThisSignature()
    {
        // The first object names its signer and format after its contents; the second has neither.
        using X509Certificate2 certificate = Certificate();
        byte[] first = SignedPdf(certificate, name: "First Signer");
        string secondText = Encoding.Latin1.GetString(SignedPdf(certificate)).Replace(" /Name (Jane Doe)", string.Empty).Replace("/SubFilter /adbe.pkcs7.detached", string.Empty);
        // The second object's own ranges still point at its own bytes, so it is the same file with the entries taken out.
        PdfSignatureReport report = PdfSignatureReader.Read([.. first, .. Encoding.Latin1.GetBytes(secondText)]);

        Assert.AreEqual(2, report.Signatures.Count);
        Assert.AreEqual("First Signer", report.Signatures[0].Name);
        Assert.AreEqual(string.Empty, report.Signatures[1].Name, "The first object's /Name must not leak into the second.");
        Assert.AreEqual(string.Empty, report.Signatures[1].SubFilter, "The first object's /SubFilter must not leak into the second.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANameWrittenAsUtf16_IsDecoded()
    {
        Assert.AreEqual("Jane", PdfSignatureReader.DecodePdfString("þÿ\0J\0a\0n\0e"));
        Assert.AreEqual("plain", PdfSignatureReader.DecodePdfString("plain"));
        Assert.AreEqual(string.Empty, PdfSignatureReader.DecodePdfString("þÿ"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSigningTimeOfTheSignature_IsShownInUtc()
    {
        using X509Certificate2 certificate = Certificate();

        PdfSignature signature = Only(PdfSignatureReader.Read(SignedPdf(certificate)));

        Assert.AreEqual("2026-06-01 12:30:15 UTC", signature.SigningTime);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASignatureWithoutASigningTime_ShowsNone()
    {
        using X509Certificate2 certificate = Certificate();

        PdfSignature signature = Only(PdfSignatureReader.Read(SignedPdf(certificate, withSigningTime: false)));

        Assert.IsNull(signature.SigningTime);
        Assert.IsFalse(PdfSignatureReader.Describe(PdfSignatureReader.Read(SignedPdf(certificate, withSigningTime: false))).Any(l => l.Contains("Signed ")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LooksLikePdf_NeedsTheHeader()
    {
        Assert.IsTrue(PdfSignatureReader.LooksLikePdf("%PDF-1.4\n"u8));
        Assert.IsFalse(PdfSignatureReader.LooksLikePdf("plain text"u8));
    }

    private static string RangeIn(string text)
    {
        int start = text.IndexOf("[0 ", text.IndexOf("/ByteRange", StringComparison.Ordinal), StringComparison.Ordinal);
        return text[start..(text.IndexOf(']', start) + 1)];
    }
}
