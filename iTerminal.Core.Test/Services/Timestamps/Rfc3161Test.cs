using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using iTerminal.Core.Services.Timestamps;

namespace iTerminal.Core.Test.Services.Timestamps;

[TestClass]
public class Rfc3161Test
{
    private static readonly byte[] Digest = SHA256.HashData("the evidence"u8);
    private static readonly BigInteger Nonce = new(12345);

    private readonly FakeTsa _tsa = new();

    [TestMethod]
    public void AGrantedAnswerBecomesATokenThatVerifiesForTheDigest()
    {
        var token = _tsa.Token(Digest, Nonce);

        var timestamp = Rfc3161.ParseResponse(FakeTsa.Response(0, token), Digest, Nonce, FakeTsa.Address);

        Assert.AreEqual(FakeTsa.Address, timestamp.Authority);
        Assert.AreEqual(FakeTsa.GeneratedAt.UtcDateTime, timestamp.GeneratedUtc);
        Assert.AreEqual(Convert.ToBase64String(token), timestamp.Token);
        Assert.IsTrue(Rfc3161.IsValidFor(timestamp, Digest));
    }

    [TestMethod]
    public void AGrantedWithModificationsIsAcceptedToo()
    {
        var token = _tsa.Token(Digest, Nonce);

        var timestamp = Rfc3161.ParseResponse(FakeTsa.Response(1, token), Digest, Nonce, FakeTsa.Address);

        Assert.IsTrue(Rfc3161.IsValidFor(timestamp, Digest));
    }

    [TestMethod]
    public void ATokenDoesNotVerifyForAnotherDigest()
    {
        var timestamp = Rfc3161.ParseResponse(FakeTsa.Response(0, _tsa.Token(Digest, Nonce)), Digest, Nonce, FakeTsa.Address);

        Assert.IsFalse(Rfc3161.IsValidFor(timestamp, SHA256.HashData("another file"u8)));
    }

    [TestMethod]
    public void ATokenWhoseSignatureWasAlteredDoesNotVerify()
    {
        var token = _tsa.Token(Digest, Nonce);
        var timestamp = Rfc3161.ParseResponse(FakeTsa.Response(0, token), Digest, Nonce, FakeTsa.Address);
        var altered = (byte[])token.Clone();
        altered[^1] ^= 0x01;
        var forged = timestamp with { Token = Convert.ToBase64String(altered) };

        Assert.IsFalse(Rfc3161.IsValidFor(forged, Digest));
    }

    [TestMethod]
    public void ATokenNotInBase64DoesNotVerify()
    {
        var timestamp = new Rfc3161Timestamp(FakeTsa.Address, FakeTsa.GeneratedAt.UtcDateTime, "not base64 !!");

        Assert.IsFalse(Rfc3161.IsValidFor(timestamp, Digest));
    }

    [TestMethod]
    public void ARefusalIsAnError()
    {
        var exception = Assert.ThrowsExactly<TimestampException>(() =>
            Rfc3161.ParseResponse(FakeTsa.Response(2, null), Digest, Nonce, FakeTsa.Address));

        StringAssert.Contains(exception.Message, "refused");
    }

    [TestMethod]
    public void AnAnswerWithoutATokenIsAnError()
    {
        Assert.ThrowsExactly<TimestampException>(() =>
            Rfc3161.ParseResponse(FakeTsa.Response(0, null), Digest, Nonce, FakeTsa.Address));
    }

    [TestMethod]
    public void ATokenForAnotherDigestIsRejectedWhenAnswered()
    {
        var other = SHA256.HashData("another file"u8);

        Assert.ThrowsExactly<TimestampException>(() =>
            Rfc3161.ParseResponse(FakeTsa.Response(0, _tsa.Token(other, Nonce)), Digest, Nonce, FakeTsa.Address));
    }

    [TestMethod]
    public void ATokenAnsweringAnotherRequestIsRejected()
    {
        Assert.ThrowsExactly<TimestampException>(() =>
            Rfc3161.ParseResponse(FakeTsa.Response(0, _tsa.Token(Digest, new BigInteger(999))), Digest, Nonce, FakeTsa.Address));
    }

    [TestMethod]
    public void ATokenWithoutANonceIsRejectedWhenOneWasSent()
    {
        Assert.ThrowsExactly<TimestampException>(() =>
            Rfc3161.ParseResponse(FakeTsa.Response(0, _tsa.Token(Digest, null)), Digest, Nonce, FakeTsa.Address));
    }

    [TestMethod]
    public void GarbageIsAnErrorNotACrash()
    {
        Assert.ThrowsExactly<TimestampException>(() =>
            Rfc3161.ParseResponse(new byte[] { 0x01, 0x02, 0x03 }, Digest, Nonce, FakeTsa.Address));
    }

    [TestMethod]
    public void TheRequestCarriesTheSha256ImprintTheNonceAndCertReq()
    {
        var request = Rfc3161.BuildRequest(Digest, Nonce);

        var outer = new AsnReader(request, AsnEncodingRules.DER).ReadSequence();
        Assert.AreEqual(1, outer.ReadInteger());
        var imprint = outer.ReadSequence();
        var algorithm = imprint.ReadSequence();
        Assert.AreEqual(FakeTsa.Sha256Oid, algorithm.ReadObjectIdentifier());
        CollectionAssert.AreEqual(Digest, imprint.ReadOctetString());
        Assert.AreEqual(Nonce, outer.ReadInteger());
        Assert.IsTrue(outer.ReadBoolean());
        Assert.IsFalse(outer.HasData);
    }

    [TestMethod]
    public void TheRequestRefusesADigestThatIsNotSha256()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Rfc3161.BuildRequest(new byte[20], Nonce));
    }
}
