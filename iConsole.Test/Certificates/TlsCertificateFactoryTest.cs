using System.Net;
using System.Security.Cryptography.X509Certificates;
using iConsole.Certificates;

namespace iConsole.Test.Certificates;

[TestClass]
public class TlsCertificateFactoryTest
{
    private static readonly IPAddress Address = IPAddress.Parse("192.168.1.20");

    [TestMethod]
    public void CreateSelfSigned_IsSelfSignedForTheAddress()
    {
        using X509Certificate2 certificate = TlsCertificateFactory.CreateSelfSigned(Address, DateTimeOffset.UtcNow, TimeSpan.FromDays(1));

        Assert.AreEqual(certificate.Subject, certificate.Issuer);
        Assert.IsTrue(certificate.Extensions.OfType<X509Extension>().Any(extension => extension.Oid?.Value == "2.5.29.17"), "subject alternative name");
        Assert.IsTrue(certificate.ToString(true).Contains("192.168.1.20", StringComparison.Ordinal), "the address is in the SAN");
    }

    [TestMethod]
    public void CreateSelfSigned_StartsAnHourBackAndEndsAfterTheValidity()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using X509Certificate2 certificate = TlsCertificateFactory.CreateSelfSigned(Address, now, TimeSpan.FromDays(1));

        Assert.IsTrue(certificate.NotBefore.ToUniversalTime() <= now.UtcDateTime.AddMinutes(-59), "clock skew allowance");
        Assert.IsTrue(certificate.NotAfter.ToUniversalTime() >= now.UtcDateTime.AddHours(23), "one day of validity");
    }

    [TestMethod]
    public void CreateSelfSigned_KeepsAUsablePrivateKey()
    {
        using X509Certificate2 certificate = TlsCertificateFactory.CreateSelfSigned(Address, DateTimeOffset.UtcNow, TimeSpan.FromDays(1));

        Assert.IsTrue(certificate.HasPrivateKey);
        Assert.IsNotNull(certificate.GetECDsaPrivateKey());
    }

    [TestMethod]
    public void CreateSelfSigned_ZeroValidity_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TlsCertificateFactory.CreateSelfSigned(Address, DateTimeOffset.UtcNow, TimeSpan.Zero));
    }
}
