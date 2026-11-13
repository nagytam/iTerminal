using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iConsole.Test.Core.Certificates;

namespace iConsole.Test.Certificates;

/// <summary>The fixed clock and the chain helper for the reader tests; the certificates themselves come from the shared helper.</summary>
internal static class CertificateTestData
{
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A self-signed RSA-2048 server certificate valid around <see cref="Now"/>.</summary>
    public static X509Certificate2 SelfSignedRsa(string commonName = "example.test", int daysBefore = 30, int daysAfter = 30) =>
        TestCertificates.SelfSignedRsa(commonName, daysBefore, daysAfter, Now);

    /// <summary>A certificate authority and an RSA leaf it signs, to exercise a chain in one bundle.</summary>
    public static (X509Certificate2 Authority, X509Certificate2 Leaf) AuthorityAndLeaf()
    {
        using RSA authorityKey = RSA.Create(2048);
        CertificateRequest authorityRequest = new("CN=Test Root", authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
        authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        X509Certificate2 authority = authorityRequest.CreateSelfSigned(Now.AddDays(-30), Now.AddDays(365));

        using RSA leafKey = RSA.Create(2048);
        CertificateRequest leafRequest = new("CN=leaf.test", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        byte[] serial = RandomNumberGenerator.GetBytes(8);
        X509Certificate2 leaf = leafRequest.Create(authority, Now.AddDays(-1), Now.AddDays(90), serial);
        return (authority, leaf);
    }

    public static string Pem(X509Certificate2 certificate) => certificate.ExportCertificatePem();

    public static string PrivateKeyPem() => TestCertificates.PrivateKeyPem();
}
