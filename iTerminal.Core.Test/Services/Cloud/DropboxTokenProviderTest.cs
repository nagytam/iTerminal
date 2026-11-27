using System.Net;
using iConsole.Services;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Test.Helper;
using iTerminal.Core.Test.Services.Cloud;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Services.Cloud.Test;

/// <summary>Where a Dropbox access token comes from: the app key, the password manager's refresh token, then Dropbox itself.</summary>
[TestClass]
public class DropboxTokenProviderTest
{
    private static Mock<IPasswordManager> Vault(bool unlocked, params PasswordEntry[] entries)
    {
        var vault = new Mock<IPasswordManager>();
        vault.SetupGet(v => v.IsUnlocked).Returns(unlocked);
        vault.Setup(v => v.List()).Returns(entries);
        return vault;
    }

    [TestMethod]
    public void PickRefreshToken_ChoosesTheDropboxEntryOfTheAccount_OrTheFirstForTheDefault()
    {
        var entries = new[]
        {
            ("Bank", "someone", "not-this"),
            ("Dropbox personal", "personal", "rt-personal"),
            ("Dropbox work", "work", "rt-work"),
            ("Dropbox empty", "nothing", string.Empty),
        };

        Assert.AreEqual("rt-work", DropboxTokenProvider.PickRefreshToken(entries, "work"));
        Assert.AreEqual("rt-personal", DropboxTokenProvider.PickRefreshToken(entries, DropboxTokenProvider.DefaultAccount));
        Assert.IsNull(DropboxTokenProvider.PickRefreshToken(entries, "missing"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutAnAppKey_TheSignInSaysWhatToConfigure()
    {
        var provider = new DropboxTokenProvider(new OAuthClientOptions(), Vault(true).Object);

        var error = await AsyncAssert.ThrowsAsync<CloudSignInRequiredException>(() => provider.GetAccessTokenAsync("default", CancellationToken.None));

        StringAssert.Contains(error.Message, "Cloud:Dropbox:ClientId");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithTheVaultLocked_TheSignInAsksForIt()
    {
        var provider = new DropboxTokenProvider(new OAuthClientOptions { ClientId = "app" }, Vault(false).Object);

        var error = await AsyncAssert.ThrowsAsync<CloudSignInRequiredException>(() => provider.GetAccessTokenAsync("default", CancellationToken.None));

        StringAssert.Contains(error.Message, "Unlock the password manager");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARefreshToken_IsExchangedOnce_ThenTheAccessTokenIsKept()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK, """{"access_token":"AT-1","expires_in":14400}""", "application/json"));
        var vault = Vault(true, new PasswordEntry { Title = "Dropbox", Username = "default", Password = "RT-1" });
        var provider = new DropboxTokenProvider(new OAuthClientOptions { ClientId = "app-key" }, vault.Object, handler: fake);

        var first = await provider.GetAccessTokenAsync("default", CancellationToken.None);
        var second = await provider.GetAccessTokenAsync("default", CancellationToken.None);

        Assert.AreEqual("AT-1", first);
        Assert.AreEqual("AT-1", second);
        Assert.AreEqual(1, fake.Requests.Count, "The second call is served from memory.");
        StringAssert.Contains(fake.Requests[0].Text, "grant_type=refresh_token");
        StringAssert.Contains(fake.Requests[0].Text, "refresh_token=RT-1");
        StringAssert.Contains(fake.Requests[0].Text, "client_id=app-key");
        Assert.IsFalse(fake.Requests[0].Text.Contains("client_secret", StringComparison.Ordinal), "A public client has no secret.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARefusedRefreshToken_AsksForANewOne()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""", "application/json"));
        var vault = Vault(true, new PasswordEntry { Title = "Dropbox", Username = "default", Password = "old" });
        var provider = new DropboxTokenProvider(new OAuthClientOptions { ClientId = "app-key" }, vault.Object, handler: fake);

        var error = await AsyncAssert.ThrowsAsync<CloudSignInRequiredException>(() => provider.GetAccessTokenAsync("default", CancellationToken.None));

        StringAssert.Contains(error.Message, "refused the refresh token");
    }
}
