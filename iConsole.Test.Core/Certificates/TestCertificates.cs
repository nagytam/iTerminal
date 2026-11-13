using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace iConsole.Test.Core.Certificates;

/// <summary>Certificates generated at run time, shared by every test project, so no fixture holds real key material.</summary>
public static class TestCertificates
{
    /// <summary>A self-signed RSA-2048 server certificate with a DNS and an IP name, valid for the given days around now.</summary>
    public static X509Certificate2 SelfSignedRsa(string commonName = "example.test", int daysBefore = 30, int daysAfter = 30, DateTimeOffset? now = null)
    {
        DateTimeOffset center = now ?? DateTimeOffset.UtcNow;
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        SubjectAlternativeNameBuilder names = new();
        names.AddDnsName(commonName);
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        return request.CreateSelfSigned(center.AddDays(-daysBefore), center.AddDays(daysAfter));
    }

    /// <summary>The PEM text of <see cref="SelfSignedRsa"/> for <paramref name="commonName"/>.</summary>
    public static string SelfSignedRsaPem(string commonName = "example.test")
    {
        using X509Certificate2 certificate = SelfSignedRsa(commonName);
        return certificate.ExportCertificatePem();
    }

    /// <summary>A PKCS #8 private key in PEM. It is never a certificate.</summary>
    public static string PrivateKeyPem()
    {
        using RSA key = RSA.Create(2048);
        return key.ExportPkcs8PrivateKeyPem();
    }
}
