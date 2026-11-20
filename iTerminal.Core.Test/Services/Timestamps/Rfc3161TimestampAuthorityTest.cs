using System.Formats.Asn1;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Timestamps;
using Moq;

namespace iTerminal.Core.Test.Services.Timestamps;

[TestClass]
public class Rfc3161TimestampAuthorityTest
{
    private static readonly byte[] Digest = SHA256.HashData("the evidence"u8);

    private readonly FakeTsa _tsa = new();

    /// <summary>Answers each POST with a TimeStampResp for the request it was sent, and remembers what it was sent.</summary>
    private sealed class AnsweringHandler(FakeTsa tsa, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Addresses { get; } = [];

        public string? ContentType { get; private set; }

        public byte[]? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Addresses.Add(request.RequestUri!);
            ContentType = request.Content!.Headers.ContentType?.MediaType;
            RequestBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            if (status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(status);
            }

            // TimeStampReq: version, MessageImprint (algorithm, digest), nonce, certReq.
            var reader = new AsnReader(RequestBody, AsnEncodingRules.DER).ReadSequence();
            reader.ReadInteger();
            var imprint = reader.ReadSequence();
            imprint.ReadSequence();
            var digest = imprint.ReadOctetString();
            var nonce = reader.ReadInteger();

            var body = FakeTsa.Response(0, tsa.Token(digest, nonce));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        }
    }

    private static Mock<ISettingsService> Settings(string? address)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.Get(Rfc3161TimestampAuthority.SettingKey)).Returns(address);
        return settings;
    }

    [TestMethod]
    public async Task ItPostsARequestAndKeepsTheTokenItGetsBack()
    {
        var handler = new AnsweringHandler(_tsa);
        using var authority = new Rfc3161TimestampAuthority(Settings(FakeTsa.Address).Object, handler);

        var timestamp = await authority.TimestampAsync(Digest);

        Assert.IsNotNull(timestamp);
        Assert.AreEqual(new Uri(FakeTsa.Address), handler.Addresses.Single());
        Assert.AreEqual("application/timestamp-query", handler.ContentType);
        Assert.AreEqual(FakeTsa.Address, timestamp.Authority);
        Assert.IsTrue(Rfc3161.IsValidFor(timestamp, Digest));
    }

    [TestMethod]
    public async Task AnEmptySettingTurnsTimeStampingOffWithoutAnyRequest()
    {
        var handler = new AnsweringHandler(_tsa);
        using var authority = new Rfc3161TimestampAuthority(Settings("  ").Object, handler);

        var timestamp = await authority.TimestampAsync(Digest);

        Assert.IsNull(timestamp);
        Assert.AreEqual(0, handler.Addresses.Count);
    }

    [TestMethod]
    public async Task WithNoSettingTheDefaultAuthorityIsAsked()
    {
        var handler = new AnsweringHandler(_tsa);
        using var authority = new Rfc3161TimestampAuthority(Settings(null).Object, handler);

        await authority.TimestampAsync(Digest);

        Assert.AreEqual(new Uri(Rfc3161TimestampAuthority.DefaultAuthority), handler.Addresses.Single());
    }

    [TestMethod]
    public async Task AnAddressThatIsNotWebIsAnError()
    {
        using var authority = new Rfc3161TimestampAuthority(Settings("file:///etc/passwd").Object, new AnsweringHandler(_tsa));

        await Assert.ThrowsExactlyAsync<TimestampException>(() => authority.TimestampAsync(Digest));
    }

    [TestMethod]
    public async Task AnHttpErrorIsATimestampException()
    {
        using var authority = new Rfc3161TimestampAuthority(Settings(FakeTsa.Address).Object, new AnsweringHandler(_tsa, HttpStatusCode.InternalServerError));

        await Assert.ThrowsExactlyAsync<TimestampException>(() => authority.TimestampAsync(Digest));
    }

    [TestMethod]
    public async Task AnUnreachableAuthorityIsATimestampExceptionNotAnUnhandledFault()
    {
        using var authority = new Rfc3161TimestampAuthority(Settings(FakeTsa.Address).Object, new FailingHandler());

        var exception = await Assert.ThrowsExactlyAsync<TimestampException>(() => authority.TimestampAsync(Digest));

        StringAssert.Contains(exception.Message, "tsa.example");
    }

    [TestMethod]
    public async Task ACancelledCallStaysCancelled()
    {
        using var authority = new Rfc3161TimestampAuthority(Settings(FakeTsa.Address).Object, new FailingHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await authority.TimestampAsync(Digest, cancellation.Token);
            Assert.Fail("A cancelled request must not return a result.");
        }
        catch (OperationCanceledException)
        {
            // Expected: the caller's cancellation is not turned into a TimestampException.
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("no route to host"));
    }
}
