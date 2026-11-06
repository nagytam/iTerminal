using System.Net;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Reputation;
using Moq;

namespace iTerminal.Core.Test.Services.Reputation;

/// <summary>The VirusTotal look-up over a fake transport: no test here reaches the network.</summary>
[TestClass]
public class VirusTotalReputationServiceTest
{
    private static readonly string Hash = new('c', 64);

    private const string Found = """
        {"data":{"attributes":{"last_analysis_stats":{"malicious":3,"suspicious":1,"undetected":60,"harmless":6,"timeout":2},
        "first_submission_date":1577966400,"last_submission_date":1772625600,"last_analysis_date":1772000000}}}
        """;

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request, cancellationToken);
        }
    }

    private static (VirusTotalReputationService Service, FakeHandler Handler) Make(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        FakeHandler handler = new(respond);
        return (new VirusTotalReputationService(new HttpClient(handler)), handler);
    }

    private static Task<HttpResponseMessage> Reply(HttpStatusCode code, string body = "") =>
        Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });

    [TestMethod]
    public async Task AnAnswer_GivesDetectionsTotalAndDates()
    {
        var (service, _) = Make((_, _) => Reply(HttpStatusCode.OK, Found));

        var result = await service.LookUpAsync(Hash, "secret-key");

        Assert.AreEqual(ReputationStatus.Found, result.Status);
        Assert.AreEqual(4, result.Detections);
        Assert.AreEqual(70, result.Total);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1577966400), result.FirstSeen);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1772625600), result.LastSeen);
        Assert.AreEqual("https://www.virustotal.com/gui/file/" + Hash, result.Link);
    }

    [TestMethod]
    public async Task OnlyTheHashIsInTheAddress_AndTheKeyIsInTheHeader()
    {
        var (service, handler) = Make((_, _) => Reply(HttpStatusCode.OK, Found));

        await service.LookUpAsync(Hash.ToUpperInvariant(), "secret-key");

        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("https://www.virustotal.com/api/v3/files/" + Hash, request.RequestUri!.ToString());
        Assert.IsFalse(request.RequestUri.ToString().Contains("secret-key"));
        Assert.AreEqual("secret-key", request.Headers.GetValues("x-apikey").Single());
        Assert.IsNull(request.Content, "a look-up never carries file contents");
    }

    [TestMethod]
    public async Task ANotSha256_IsRefusedWithoutAnyRequest()
    {
        var (service, handler) = Make((_, _) => Reply(HttpStatusCode.OK, Found));

        var result = await service.LookUpAsync("abc", "key");

        Assert.AreEqual(ReputationStatus.Error, result.Status);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task NotFound_CarriesThePageToUploadFrom()
    {
        var (service, _) = Make((_, _) => Reply(HttpStatusCode.NotFound));

        var result = await service.LookUpAsync(Hash, "key");

        Assert.AreEqual(ReputationStatus.NotFound, result.Status);
        Assert.IsNotNull(result.Link);
    }

    [TestMethod]
    public async Task RateLimit_ReadsRetryAfter_WhenGiven()
    {
        var (service, _) = Make((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return Task.FromResult(response);
        });

        var result = await service.LookUpAsync(Hash, "key");

        Assert.AreEqual(ReputationStatus.RateLimited, result.Status);
        Assert.AreEqual(TimeSpan.FromSeconds(42), result.RetryAfter);
    }

    [TestMethod]
    public async Task RateLimit_WithoutRetryAfter_HasNoWait()
    {
        var (service, _) = Make((_, _) => Reply(HttpStatusCode.TooManyRequests));

        var result = await service.LookUpAsync(Hash, "key");

        Assert.AreEqual(ReputationStatus.RateLimited, result.Status);
        Assert.IsNull(result.RetryAfter);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden)]
    public async Task ARefusedKey_IsInvalidKey(HttpStatusCode code)
    {
        var (service, _) = Make((_, _) => Reply(code));

        Assert.AreEqual(ReputationStatus.InvalidKey, (await service.LookUpAsync(Hash, "key")).Status);
    }

    [TestMethod]
    public async Task ANetworkFailure_IsOffline_NotAnException()
    {
        var (service, _) = Make((_, _) => throw new HttpRequestException("no route"));

        Assert.AreEqual(ReputationStatus.Offline, (await service.LookUpAsync(Hash, "key")).Status);
    }

    [TestMethod]
    public async Task ATimeout_IsOffline()
    {
        var (service, _) = Make((_, _) => throw new TaskCanceledException("timeout"));

        Assert.AreEqual(ReputationStatus.Offline, (await service.LookUpAsync(Hash, "key")).Status);
    }

    [TestMethod]
    public async Task Cancelling_Cancels()
    {
        var (service, _) = Make(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        });
        using CancellationTokenSource cancellation = new();

        var pending = service.LookUpAsync(Hash, "key", cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => pending);
    }

    [TestMethod]
    public async Task AnUnexpectedBody_IsAnError()
    {
        var (service, _) = Make((_, _) => Reply(HttpStatusCode.OK, "{\"data\":{}}"));

        Assert.AreEqual(ReputationStatus.Error, (await service.LookUpAsync(Hash, "key")).Status);
    }

    [TestMethod]
    public void TheKey_IsTheFirstVirusTotalEntrysPassword_AndNothingWhileLocked()
    {
        Mock<IPasswordManager> passwords = new();
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        passwords.Setup(p => p.List()).Returns(
        [
            new PasswordEntry { Title = "S3 backup", Password = "no" },
            new PasswordEntry { Title = "virustotal personal", Password = " the-key " },
        ]);

        Assert.AreEqual("the-key", VirusTotalKey.Resolve(passwords.Object));

        passwords.SetupGet(p => p.IsUnlocked).Returns(false);
        Assert.IsNull(VirusTotalKey.Resolve(passwords.Object));
        Assert.IsNull(VirusTotalKey.Resolve(null));
    }
}
