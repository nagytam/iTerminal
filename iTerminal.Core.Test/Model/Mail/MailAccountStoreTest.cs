using Moq;
using iTerminal.Core.Model.Mail;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Model.Mail;

[TestClass]
public class MailAccountStoreTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void AnAccountSavedIsThereWhenTheStoreIsReopened()
    {
        string? saved = null;
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Set(MailAccountStore.SettingsKey, It.IsAny<string>())).Callback<string, string>((_, v) => saved = v);
        settings.Setup(s => s.Get(MailAccountStore.SettingsKey)).Returns(() => saved);
        var account = new MailAccount { Name = "Work", PopHost = "pop.example.test", SmtpHost = "smtp.example.test", UserName = "ada" };

        new MailAccountStore(settings.Object).Save(account);
        var reopened = new MailAccountStore(settings.Object).Accounts.Single();

        Assert.AreEqual(account, reopened);
        Assert.AreEqual("mail:" + account.Id, reopened.SecretProfileId);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SavingAgainReplacesTheAccountWithTheSameId()
    {
        var settings = new Mock<ISettingsService>();
        var store = new MailAccountStore(settings.Object);
        var account = new MailAccount { Name = "Work" };
        store.Save(account);

        store.Save(account with { Name = "Renamed" });

        Assert.AreEqual("Renamed", store.Accounts.Single().Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSettingsValueNeverHoldsAPassword()
    {
        string? saved = null;
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Set(MailAccountStore.SettingsKey, It.IsAny<string>())).Callback<string, string>((_, v) => saved = v);
        var store = new MailAccountStore(settings.Object);

        store.Save(new MailAccount { Name = "Work", UserName = "ada" });

        StringAssert.DoesNotMatch(saved!, new System.Text.RegularExpressions.Regex("password", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }
}
