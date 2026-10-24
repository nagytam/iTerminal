using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Cloud;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Services.Cloud.Test;

/// <summary>What an S3 request is signed with: the password manager first, then the environment, then nothing — never the URL.</summary>
[TestClass]
public class S3CredentialResolverTest
{
    private static Mock<IPasswordManager> Vault(params PasswordEntry[] entries)
    {
        var vault = new Mock<IPasswordManager>();
        vault.SetupGet(v => v.IsUnlocked).Returns(true);
        vault.Setup(v => v.List()).Returns(entries);
        return vault;
    }

    private static Func<string, string?> Env(Dictionary<string, string> values) =>
        name => values.TryGetValue(name, out var value) ? value : null;

    [TestMethod]
    public void AnS3EntryInThePasswordManager_SuppliesTheKeyAndSecretAndItsNotes()
    {
        var vault = Vault(new PasswordEntry
        {
            Title = "S3 backup", Username = "AKIA1", Password = "secret1",
            Notes = "endpoint=https://minio.local:9000\nregion=eu-west-1\ntoken=tok",
        });

        var credentials = S3CredentialResolver.Resolve(null, vault.Object, null, Env([]));

        Assert.AreEqual("AKIA1", credentials.AccessKeyId);
        Assert.AreEqual("secret1", credentials.SecretAccessKey);
        Assert.AreEqual("tok", credentials.SessionToken);
        Assert.AreEqual("eu-west-1", credentials.Region);
        Assert.AreEqual("https://minio.local:9000", credentials.Endpoint);
    }

    [TestMethod]
    public void AnAccessKeyInTheUrl_SelectsItsOwnEntry()
    {
        var vault = Vault(
            new PasswordEntry { Title = "S3 one", Username = "AKIA1", Password = "s1" },
            new PasswordEntry { Title = "S3 two", Username = "AKIA2", Password = "s2" });

        var credentials = S3CredentialResolver.Resolve("AKIA2", vault.Object, null, Env([]));

        Assert.AreEqual("s2", credentials.SecretAccessKey);
    }

    [TestMethod]
    public void TheStandardEnvironment_IsUsedWhenTheVaultHasNothing()
    {
        var credentials = S3CredentialResolver.Resolve(null, Vault().Object, null, Env(new()
        {
            ["AWS_ACCESS_KEY_ID"] = "ENVKEY",
            ["AWS_SECRET_ACCESS_KEY"] = "envsecret",
            ["AWS_SESSION_TOKEN"] = "envtoken",
        }));

        Assert.AreEqual("ENVKEY", credentials.AccessKeyId);
        Assert.AreEqual("envsecret", credentials.SecretAccessKey);
        Assert.AreEqual("envtoken", credentials.SessionToken);
    }

    [TestMethod]
    public void AnEnvironmentKeyNotTheOneTheUrlNames_IsNotUsed()
    {
        var credentials = S3CredentialResolver.Resolve("SOMEONEELSE", Vault().Object, null, Env(new()
        {
            ["AWS_ACCESS_KEY_ID"] = "ENVKEY",
            ["AWS_SECRET_ACCESS_KEY"] = "envsecret",
        }));

        Assert.IsNull(credentials.AccessKeyId, "a request for another account is not signed with this one");
        Assert.IsNull(credentials.SecretAccessKey);
    }

    [TestMethod]
    public void WithNothingAtAll_TheRequestIsUnsigned()
    {
        var credentials = S3CredentialResolver.Resolve(null, null, null, Env([]));

        Assert.IsNull(credentials.AccessKeyId);
        Assert.IsNull(credentials.SecretAccessKey);
    }

    [TestMethod]
    public void RegionAndEndpoint_FallBackToConfigurationWhenNothingElseSaysSo()
    {
        var options = new S3Options { Region = "ap-south-1", Endpoint = "https://s3.example" };

        var credentials = S3CredentialResolver.Resolve(null, null, options, Env([]));

        Assert.AreEqual("ap-south-1", credentials.Region);
        Assert.AreEqual("https://s3.example", credentials.Endpoint);
    }

    [TestMethod]
    public void TheEnvironmentEndpoint_BeatsTheConfiguredOne()
    {
        var options = new S3Options { Endpoint = "https://configured" };

        var credentials = S3CredentialResolver.Resolve(null, null, options, Env(new() { ["AWS_ENDPOINT_URL_S3"] = "https://from-env" }));

        Assert.AreEqual("https://from-env", credentials.Endpoint);
    }

    [TestMethod]
    public void TheEnvironmentRegion_BeatsTheConfiguredOne()
    {
        var options = new S3Options { Region = "ap-south-1" };

        var credentials = S3CredentialResolver.Resolve(null, null, options, Env(new() { ["AWS_REGION"] = "us-west-2" }));

        Assert.AreEqual("us-west-2", credentials.Region);
    }
}
