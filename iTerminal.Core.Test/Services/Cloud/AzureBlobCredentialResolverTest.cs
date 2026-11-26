using iConsole.Services;
using iTerminal.Core.Model.Passwords;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Services.Cloud.Test;

/// <summary>What an Azure Blob request is signed with: the password manager's entry for the account, then the environment, then nothing.</summary>
[TestClass]
public class AzureBlobCredentialResolverTest
{
    private static Mock<IPasswordManager> Vault(bool unlocked, params PasswordEntry[] entries)
    {
        var vault = new Mock<IPasswordManager>();
        vault.SetupGet(v => v.IsUnlocked).Returns(unlocked);
        vault.Setup(v => v.List()).Returns(entries);
        return vault;
    }

    private static Func<string, string?> Env(Dictionary<string, string> values) =>
        name => values.TryGetValue(name, out var value) ? value : null;

    [TestMethod]
    public void AnAzureEntryForTheAccount_SuppliesTheKeyAndItsEndpoint()
    {
        var vault = Vault(true,
            new PasswordEntry { Title = "Azure other", Username = "otheracc", Password = "k-other" },
            new PasswordEntry { Title = "Azure photos", Username = "photos", Password = "k-photos", Notes = "endpoint=http://127.0.0.1:10000\nunrelated=x" });

        var credentials = AzureBlobCredentialResolver.Resolve("photos", vault.Object, null, Env([]));

        Assert.AreEqual("photos", credentials.Account);
        Assert.AreEqual("k-photos", credentials.Key);
        Assert.AreEqual("http://127.0.0.1:10000", credentials.Endpoint);
    }

    [TestMethod]
    public void WithNoEntry_TheEnvironmentGivesTheKeyOfThatAccountOnly()
    {
        var vault = Vault(true);
        var env = Env(new Dictionary<string, string> { ["AZURE_STORAGE_ACCOUNT"] = "envacc", ["AZURE_STORAGE_KEY"] = "env-key" });

        Assert.AreEqual("env-key", AzureBlobCredentialResolver.Resolve("envacc", vault.Object, null, env).Key);
        Assert.IsNull(AzureBlobCredentialResolver.Resolve("other", vault.Object, null, env).Key, "Another account gets anonymous requests.");
    }

    [TestMethod]
    public void WithNothing_RequestsAreAnonymous_AndTheConfiguredEndpointApplies()
    {
        var credentials = AzureBlobCredentialResolver.Resolve("nobody", Vault(true).Object, new AzureBlobOptions { Endpoint = "https://blob.example" }, Env([]));

        Assert.IsNull(credentials.Key);
        Assert.AreEqual("https://blob.example", credentials.Endpoint);
    }

    [TestMethod]
    public void SavedAccounts_ListsTheAzureEntriesOnceAndSorted_AndNothingWhileLocked()
    {
        var vault = Vault(true,
            new PasswordEntry { Title = "Azure b", Username = "beta", Password = "x" },
            new PasswordEntry { Title = "azure a", Username = "alpha", Password = "x" },
            new PasswordEntry { Title = "Azure dup", Username = "beta", Password = "y" },
            new PasswordEntry { Title = "Mail", Username = "gamma", Password = "z" });

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, AzureBlobCredentialResolver.SavedAccounts(vault.Object).ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), AzureBlobCredentialResolver.SavedAccounts(Vault(false).Object).ToArray());
    }
}
