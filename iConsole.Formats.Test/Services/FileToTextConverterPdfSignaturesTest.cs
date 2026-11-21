using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using iConsole.Formats.Services;

using iConsole.Test.Services;
namespace iConsole.Formats.Test.Services;

[TestClass]
public class FileToTextConverterPdfSignaturesTest
{
    private static readonly string TestFilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Services", "TestFiles");

    private static string WriteSignedPdf(string directory)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new("CN=Converter Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx, "x"), "x");

        const int hexLength = 8192;
        string head = "%PDF-1.7\n1 0 obj\n<< /Type /Sig /SubFilter /adbe.pkcs7.detached /ByteRange [0 0000000000 0000000000 0000000000] /Contents ";
        string tail = "\n>>\nendobj\n%%EOF\n";
        long second = head.Length;
        long third = head.Length + 1 + hexLength + 1;
        head = head.Replace("[0 0000000000 0000000000 0000000000]", $"[0 {second:D10} {third:D10} {tail.Length:D10}]");
        byte[] signed = Encoding.ASCII.GetBytes(head + tail);
        SignedCms cms = new(new ContentInfo(signed), detached: true);
        cms.ComputeSignature(new CmsSigner(certificate) { IncludeOption = X509IncludeOption.EndCertOnly });
        string hex = Convert.ToHexString(cms.Encode()).PadRight(hexLength, '0');

        string path = Path.Combine(directory, "signed.pdf");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(head + "<" + hex + ">" + tail));
        return path;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASignedPdf_GivesTheSignatureSection()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string path = WriteSignedPdf(directory);

            IReadOnlyList<string> lines = await new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader())
                .BuildPdfSignatureLinesAsync(new Url("file://" + path), new LocalFileSystemAdapter(), CancellationToken.None);

            Assert.IsTrue(lines.Any(l => l.Contains("Digital signatures")), string.Join('\n', lines));
            Assert.IsTrue(lines.Any(l => l.Contains("The signed bytes are unchanged.")));
            Assert.IsTrue(lines.Any(l => l.Contains("CN=Converter Test")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnsignedPdf_GivesNothing()
    {
        IReadOnlyList<string> lines = await new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader())
            .BuildPdfSignatureLinesAsync(new Url("file://" + Path.Combine(TestFilesDir, "sample.pdf")), new LocalFileSystemAdapter(), CancellationToken.None);

        Assert.AreEqual(0, lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatIsNotNamedLikeAPdf_GivesNothing()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string signed = WriteSignedPdf(directory);
            string renamed = Path.Combine(directory, "signed.txt");
            File.Move(signed, renamed);

            IReadOnlyList<string> lines = await new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader())
                .BuildPdfSignatureLinesAsync(new Url("file://" + renamed), new LocalFileSystemAdapter(), CancellationToken.None);

            Assert.AreEqual(0, lines.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APdfNameOnSomethingThatIsNotAPdf_GivesNothing()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string path = Path.Combine(directory, "fake.pdf");
            File.WriteAllText(path, "just text with /ByteRange [0 1 2 3] in it");

            IReadOnlyList<string> lines = await new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader())
                .BuildPdfSignatureLinesAsync(new Url("file://" + path), new LocalFileSystemAdapter(), CancellationToken.None);

            Assert.AreEqual(0, lines.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task APdfOverTheSizeLimit_SaysSoWithoutBeingReadWhole()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string path = Path.Combine(directory, "huge.pdf");
            await using (FileStream stream = new(path, FileMode.Create))
            {
                stream.Write("%PDF-1.7\n"u8);
                stream.SetLength(iConsole.Formats.Pdf.PdfSignatureReader.MaxBytes + 1);
            }

            IReadOnlyList<string> lines = await new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader())
                .BuildPdfSignatureLinesAsync(new Url("file://" + path), new LocalFileSystemAdapter(), CancellationToken.None);

            Assert.IsTrue(lines.Any(l => l.Contains("larger than 64 MB")), string.Join('\n', lines));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadFileAsync_ForAPdf_NeverCarriesTheSignatureSection()
    {
        // The preview and the F4 editor read through ReadFileAsync, so the section must not be part of it.
        TextModel model = new();

        await new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader())
            .ReadFileAsync(new Url("file://" + Path.Combine(TestFilesDir, "sample.pdf")), new LocalFileSystemAdapter(), model);

        Assert.IsFalse(model.Lines.Any(l => l.Contains("Digital signatures")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasPdfSignatures_IsTrueForPdfNamesOnly()
    {
        Assert.IsTrue(new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader()).HasPdfSignatures(new Url("file://C:/x/a.pdf")));
        Assert.IsTrue(new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader()).HasPdfSignatures(new Url("file://C:/x/a.PDF")));
        Assert.IsFalse(new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader()).HasPdfSignatures(new Url("file://C:/x/a.txt")));
        Assert.IsFalse(new FileToTextConverterService(new iConsole.Formats.Xps.XpsDocumentReader()).HasPdfSignatures(null));
    }
}
