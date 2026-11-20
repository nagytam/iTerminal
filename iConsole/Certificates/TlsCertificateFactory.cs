using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace iConsole.Certificates;

/// <summary>
/// Creates a self-signed ECDSA P-256 certificate for a LAN address, for a TLS server that lives for one session. The
/// validity starts an hour in the past, so a phone whose clock is behind still accepts it. The certificate is returned
/// as a PFX-loaded copy, so its private key can be used by <see cref="System.Net.Security.SslStream"/> on Windows too.
/// </summary>
public static class TlsCertificateFactory
{
    public static X509Certificate2 CreateSelfSigned(IPAddress address, DateTimeOffset now, TimeSpan validity, string subjectName = "iConsole file share")
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(validity, TimeSpan.Zero);

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new($"CN={subjectName}", key, HashAlgorithmName.SHA256);
        SubjectAlternativeNameBuilder names = new();
        names.AddIpAddress(address);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

        using X509Certificate2 created = request.CreateSelfSigned(now.AddHours(-1), now.Add(validity));
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.EphemeralKeySet);
    }
}
