using System.Net;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Cloud;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Services.Cloud;

[TestClass]
public class OAuthTokenProviderTest
{
    private static OAuthProviderConfig Config(string? clientId = "client", string? secret = null) => new(
        "Test Cloud", "https://idp.test/device", "https://idp.test/token", "scope", clientId, secret, "set the id.");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        FakeHttpHandler.Reply(status, body, "application/json");

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(100);
        }

        Assert.IsTrue(condition(), "timed out waiting");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NoClientId_ExplainsTheRegistration()
    {
        OAuthTokenProvider provider = new(Config(clientId: " "));

        CloudSignInRequiredException exception = await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));

        StringAssert.Contains(exception.Message, "needs an app registration");
        StringAssert.Contains(exception.Message, "set the id.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshToken_IsRedeemedAndTheAccessTokenCachedUntilInvalidated()
    {
        FakeHttpHandler http = new(_ => Json("""{"access_token":"at1","expires_in":3600,"refresh_token":"rt2"}"""));
        Mock<IPasswordManager> passwords = new();
        passwords.Setup(p => p.GetSecret("cloud:testcloud:me")).Returns("rt1");
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        OAuthTokenProvider provider = new(Config(secret: "shh"), passwords.Object, handler: http);

        string first = await provider.GetAccessTokenAsync("me", CancellationToken.None);
        string second = await provider.GetAccessTokenAsync("me", CancellationToken.None);

        Assert.AreEqual("at1", first);
        Assert.AreEqual("at1", second);
        Assert.HasCount(1, http.Requests);
        StringAssert.Contains(http.Requests[0].Text, "grant_type=refresh_token");
        StringAssert.Contains(http.Requests[0].Text, "client_secret=shh");
        passwords.Verify(p => p.SaveSecret("cloud:testcloud:me", "rt2"), Times.Once());

        provider.Invalidate("me");
        await provider.GetAccessTokenAsync("me", CancellationToken.None);
        Assert.HasCount(2, http.Requests, "an invalidated token is fetched again");
        StringAssert.Contains(http.Requests[1].Text, "refresh_token=rt2", "the rotated token is used");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshToken_WithoutExpiresIn_DefaultsToAnHour()
    {
        FakeHttpHandler http = new(_ => Json("""{"access_token":"at"}"""));
        Mock<IPasswordManager> passwords = new();
        passwords.Setup(p => p.GetSecret(It.IsAny<string>())).Returns("rt");
        OAuthTokenProvider provider = new(Config(), passwords.Object, handler: http);

        await provider.GetAccessTokenAsync("me", CancellationToken.None);
        await provider.GetAccessTokenAsync("me", CancellationToken.None);

        Assert.HasCount(1, http.Requests);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshToken_RefusedByTheServer_IsForgottenAndSignInStarts()
    {
        FakeHttpHandler http = new(request => request.Uri.AbsolutePath == "/token"
            ? Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest)
            : Json("""{"device_code":"dc","user_code":"ABCD","verification_uri":"https://idp.test/activate","interval":1,"expires_in":2}"""));
        Mock<IPasswordManager> passwords = new();
        passwords.Setup(p => p.GetSecret(It.IsAny<string>())).Returns("stale");
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        Mock<INotificationService> notifications = new();
        OAuthTokenProvider provider = new(Config(), passwords.Object, notifications.Object, handler: http);

        CloudSignInRequiredException exception = await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));

        StringAssert.Contains(exception.Message, "open https://idp.test/activate and enter ABCD");
        passwords.Verify(p => p.DeleteSecret("cloud:testcloud:me"), Times.Once());
        notifications.Verify(n => n.Notify("Sign in to Test Cloud", It.IsAny<string?>(), NotificationSeverity.Info, Timeout.InfiniteTimeSpan), Times.Once());

        CloudSignInRequiredException again = await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));
        Assert.AreEqual(exception.Message, again.Message, "a sign-in under way is reported, not restarted");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SignIn_CouldNotStart_ReportsTheReason()
    {
        FakeHttpHandler http = new(_ => Json("""{"error_description":"app disabled"}""", HttpStatusCode.BadRequest));
        OAuthTokenProvider provider = new(Config(), handler: http);

        CloudSignInRequiredException exception = await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));

        StringAssert.Contains(exception.Message, "could not start: app disabled");

        FakeHttpHandler broken = new(_ => FakeHttpHandler.Reply(HttpStatusCode.BadGateway, "<html>", "text/html"));
        exception = await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => new OAuthTokenProvider(Config(), handler: broken).GetAccessTokenAsync("me", CancellationToken.None));
        StringAssert.Contains(exception.Message, "HTTP 502");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SignIn_PollsThroughPendingAndSlowDown_ThenStoresTheTokens()
    {
        int polls = 0;
        FakeHttpHandler http = new(request =>
        {
            if (request.Uri.AbsolutePath == "/device")
            {
                return Json("""{"device_code":"dc","user_code":"WXYZ","verification_url":"https://idp.test/v","interval":1,"expires_in":60}""");
            }

            return Interlocked.Increment(ref polls) switch
            {
                1 => Json("not json"),
                2 => Json("""{"error":"authorization_pending"}""", HttpStatusCode.BadRequest),
                3 => Json("""{"error":"slow_down"}""", HttpStatusCode.BadRequest),
                _ => Json("""{"access_token":"granted","expires_in":3600,"refresh_token":"rt"}"""),
            };
        });
        Mock<IPasswordManager> passwords = new();
        passwords.SetupGet(p => p.IsUnlocked).Returns(false);
        Mock<INotificationService> notifications = new();
        OAuthTokenProvider provider = new(Config(secret: "s"), passwords.Object, notifications.Object, Mock.Of<ILogger>(), http);

        await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));

        await WaitFor(() => notifications.Invocations.Any(i => i.Arguments[0] is string t && t.StartsWith("Signed in")));
        Assert.AreEqual("granted", await provider.GetAccessTokenAsync("me", CancellationToken.None));
        passwords.Verify(p => p.SaveSecret(It.IsAny<string>(), It.IsAny<string>()), Times.Never(), "a locked store is not written to");
        Assert.IsTrue(http.Requests.Any(r => r.Text.Contains("client_secret=s") && r.Text.Contains("device_code=dc")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SignIn_Refused_NotifiesTheError()
    {
        FakeHttpHandler http = new(request => request.Uri.AbsolutePath == "/device"
            ? Json("""{"device_code":"dc","user_code":"U","interval":0,"expires_in":60}""")
            : Json("""{"error":"access_denied"}""", HttpStatusCode.BadRequest));
        Mock<INotificationService> notifications = new();
        OAuthTokenProvider provider = new(Config(), notifications: notifications.Object, handler: http);

        CloudSignInRequiredException exception = await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));
        StringAssert.Contains(exception.Message, "the provider's device login page");

        await WaitFor(() => notifications.Invocations.Any(i => i.Arguments[0] is string t && t.EndsWith("sign-in failed")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SignIn_NotApprovedInTime_Expires()
    {
        FakeHttpHandler http = new(request => request.Uri.AbsolutePath == "/device"
            ? Json("""{"device_code":"dc","user_code":"U","verification_uri":"v","interval":1,"expires_in":1}""")
            : Json("""{"error":"authorization_pending"}""", HttpStatusCode.BadRequest));
        Mock<INotificationService> notifications = new();
        OAuthTokenProvider provider = new(Config(), notifications: notifications.Object, handler: http);

        await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));

        await WaitFor(() => notifications.Invocations.Any(i => i.Arguments[0] is string t && t.EndsWith("sign-in expired")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SignIn_NetworkFailureWhilePolling_IsLoggedAndEnds()
    {
        FakeHttpHandler http = new(request => request.Uri.AbsolutePath == "/device"
            ? Json("""{"device_code":"dc","user_code":"U","verification_uri":"v","interval":1,"expires_in":30}""")
            : throw new HttpRequestException("offline"));
        Mock<ILogger> logger = new();
        OAuthTokenProvider provider = new(Config(), logger: logger.Object, handler: http);

        await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));

        await WaitFor(() => logger.Invocations.Any(i => i.Method.Name == "Warning"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PasswordManagerFailures_AreTolerated()
    {
        FakeHttpHandler http = new(_ => Json("""{"access_token":"at","expires_in":3600,"refresh_token":"rt"}"""));
        Mock<IPasswordManager> passwords = new();
        passwords.Setup(p => p.GetSecret(It.IsAny<string>())).Throws<InvalidOperationException>();
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        passwords.Setup(p => p.SaveSecret(It.IsAny<string>(), It.IsAny<string>())).Throws<IOException>();
        Mock<ILogger> logger = new();
        OAuthTokenProvider provider = new(Config(), passwords.Object, logger: logger.Object, handler: http);

        // The unreadable store looks empty, so a sign-in starts (the fake answers the device endpoint with a token body, which has no device code).
        await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshToken_FailingToSaveOrDelete_IsLoggedNotThrown()
    {
        int calls = 0;
        FakeHttpHandler http = new(_ => Interlocked.Increment(ref calls) == 1
            ? Json("""{"access_token":"at","expires_in":3600,"refresh_token":"rt"}""")
            : Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest));
        Mock<IPasswordManager> passwords = new();
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        passwords.Setup(p => p.GetSecret(It.IsAny<string>())).Returns("rt0");
        passwords.Setup(p => p.SaveSecret(It.IsAny<string>(), It.IsAny<string>())).Throws<IOException>();
        passwords.Setup(p => p.DeleteSecret(It.IsAny<string>())).Throws<InvalidOperationException>();
        Mock<ILogger> logger = new();
        OAuthTokenProvider provider = new(Config(), passwords.Object, logger: logger.Object, handler: http);

        Assert.AreEqual("at", await provider.GetAccessTokenAsync("me", CancellationToken.None));
        Assert.IsTrue(logger.Invocations.Any(i => i.Method.Name == "Warning"));

        provider.Invalidate("me");
        // The in-memory token is now refused; forgetting it must survive a store that throws.
        await Assert.ThrowsExactlyAsync<CloudSignInRequiredException>(
            () => provider.GetAccessTokenAsync("me", CancellationToken.None));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProviderConfigs_UseTheirEndpoints()
    {
        OAuthProviderConfig defaultTenant = OAuthTokenProvider.OneDrive(new OAuthClientOptions { ClientId = "id" });
        OAuthProviderConfig tenant = OAuthTokenProvider.OneDrive(new OAuthClientOptions { Tenant = " contoso ", ClientSecret = "s" });
        OAuthProviderConfig google = OAuthTokenProvider.GoogleDrive(new OAuthClientOptions { ClientId = "g" });

        StringAssert.Contains(defaultTenant.TokenEndpoint, "/common/");
        StringAssert.Contains(tenant.DeviceCodeEndpoint, "/contoso/");
        Assert.AreEqual("s", tenant.ClientSecret);
        Assert.AreEqual("Google Drive", google.Name);
        Assert.AreEqual("g", google.ClientId);
        Assert.AreEqual("42", OAuthTokenProvider.Format(42));
    }
}
