using iConsole.Services;
using iTerminal.Core.Dialogs.Mail;
using iTerminal.Core.Model.Mail;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Mail;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Mail;

[TestClass]
public class MailDialogTest
{
    /// <summary>Answers from fixed data and records what the dialog asked for.</summary>
    private sealed class FakeMail : IMailService
    {
        private readonly object _lock = new();
        private readonly List<string> _sent = [];
        private readonly List<int> _deleted = [];

        public IReadOnlyList<MailListing> Listings { get; set; } =
            [new(1, 420, "Ada <ada@example.test>", "Welcome", "Tue, 14 Oct 2026 09:00:00 +0200")];

        public MailItem Item { get; set; } =
            new(1, "Ada <ada@example.test>", "you@example.test", "Welcome", "Tue, 14 Oct 2026 09:00:00 +0200", "Hello from the test.");

        public IReadOnlyList<DirectoryPerson> People { get; set; } = [new("Ada Lovelace", "ada@example.test", string.Empty)];

        public List<string> Sent { get { lock (_lock) return [.. _sent]; } }
        public List<int> Deleted { get { lock (_lock) return [.. _deleted]; } }

        public Task<IReadOnlyList<MailListing>> ListAsync(MailAccount account, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(Listings);

        public Task<MailItem> ReadAsync(MailAccount account, string password, int number, CancellationToken cancellationToken = default) =>
            Task.FromResult(Item);

        public Task DeleteAsync(MailAccount account, string password, int number, CancellationToken cancellationToken = default)
        {
            lock (_lock) _deleted.Add(number);
            return Task.CompletedTask;
        }

        public Task SendAsync(MailAccount account, string password, string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            lock (_lock) _sent.Add($"{to}|{subject}|{body}");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DirectoryPerson>> SearchDirectoryAsync(MailAccount account, string password, string term, CancellationToken cancellationToken = default) =>
            Task.FromResult(People);
    }

    private sealed class Fixture
    {
        public required MailDialog Dialog { get; init; }
        public required FakeMail Mail { get; init; }
        public required Mock<IPasswordManager> Vault { get; init; }
        public required MailAccountStore Accounts { get; init; }
    }

    private static Fixture Create(bool withAccount = true)
    {
        var settings = new Mock<ISettingsService>();
        var accounts = new MailAccountStore(settings.Object);
        if (withAccount)
        {
            accounts.Save(new MailAccount { Name = "Work", PopHost = "pop.example.test", SmtpHost = "smtp.example.test", UserName = "ada", FromAddress = "ada@example.test" });
        }

        var vault = new Mock<IPasswordManager>();
        vault.Setup(v => v.GetSecret(It.IsAny<string>())).Returns("secret");
        var mail = new FakeMail();
        return new Fixture
        {
            Dialog = new MailDialog(accounts, mail, vault.Object, TestTheme()),
            Mail = mail,
            Vault = vault,
            Accounts = accounts,
        };
    }

    private static async Task WaitIdleAsync(MailDialog dialog) =>
        await WaitUntilAsync(() => !dialog.IsBusyForTest, "the mail server conversation to finish");

    private static async Task TypeAsync(MailDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    [TestMethod]
    public async Task WithoutAnAccountTheSetupFormOpens()
    {
        var fixture = Create(withAccount: false);

        await ShowAsync(fixture.Dialog);

        Assert.AreEqual("Setup", fixture.Dialog.ModeForTest);
    }

    [TestMethod]
    public async Task WithAnAccountTheInboxLoads()
    {
        var fixture = Create();

        await ShowAsync(fixture.Dialog);

        await WaitUntilAsync(() => fixture.Dialog.ListingCountForTest == 1, "the inbox to load");
        Assert.AreEqual("List", fixture.Dialog.ModeForTest);
        StringAssert.Contains(fixture.Dialog.StatusForTest, "1 messages");
    }

    [TestMethod]
    public async Task EnterReadsTheSelectedMessage()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await WaitUntilAsync(() => fixture.Dialog.ListingCountForTest == 1, "the inbox to load");

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        await WaitUntilAsync(() => fixture.Dialog.ModeForTest == "View", "the message to open");
        StringAssert.Contains(fixture.Dialog.BodyForTest, "Hello from the test.");
    }

    [TestMethod]
    public async Task ComposeSendsTheDraftWithItsRecipientSubjectAndBody()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await WaitUntilAsync(() => fixture.Dialog.ListingCountForTest == 1, "the inbox to load");

        await fixture.Dialog.HandleKeyEventAsync(Char('c'));
        await TypeAsync(fixture.Dialog, "ada@example.test");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await TypeAsync(fixture.Dialog, "Hi");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await TypeAsync(fixture.Dialog, "Body text");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.S, 's', control: true));

        await WaitUntilAsync(() => fixture.Mail.Sent.Count == 1, "the message to be sent");
        Assert.AreEqual("ada@example.test|Hi|Body text", fixture.Mail.Sent[0]);
        Assert.AreEqual("List", fixture.Dialog.ModeForTest);
    }

    [TestMethod]
    public async Task ASendWithoutARecipientIsNotSent()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await WaitUntilAsync(() => fixture.Dialog.ListingCountForTest == 1, "the inbox to load");
        await fixture.Dialog.HandleKeyEventAsync(Char('c'));

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.S, 's', control: true));

        StringAssert.Contains(fixture.Dialog.StatusForTest, "recipient");
        Assert.AreEqual(0, fixture.Mail.Sent.Count);
    }

    [TestMethod]
    public async Task DeleteAsksOnceMoreBeforeRemovingFromTheServer()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await WaitUntilAsync(() => fixture.Dialog.ListingCountForTest == 1, "the inbox to load");

        await fixture.Dialog.HandleKeyEventAsync(Char('d'));
        Assert.AreEqual(0, fixture.Mail.Deleted.Count);
        StringAssert.Contains(fixture.Dialog.StatusForTest, "Press D again");

        await fixture.Dialog.HandleKeyEventAsync(Char('d'));
        await WaitIdleAsync(fixture.Dialog);
        CollectionAssert.AreEqual(new[] { 1 }, fixture.Mail.Deleted);
    }

    [TestMethod]
    public async Task AddressBookLookupFillsInTheRecipient()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await WaitUntilAsync(() => fixture.Dialog.ListingCountForTest == 1, "the inbox to load");
        await fixture.Dialog.HandleKeyEventAsync(Char('c'));

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.L, 'l', control: true));
        await TypeAsync(fixture.Dialog, "ada");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await WaitUntilAsync(() => fixture.Dialog.PeopleForTest.Count == 1, "the address book to answer");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual("Compose", fixture.Dialog.ModeForTest);
        Assert.AreEqual("Ada Lovelace <ada@example.test>", fixture.Dialog.DraftForTest[0]);
    }

    [TestMethod]
    public async Task ReplyAddressesTheSenderWithARePrefixedSubject()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await WaitUntilAsync(() => fixture.Dialog.ListingCountForTest == 1, "the inbox to load");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await WaitUntilAsync(() => fixture.Dialog.ModeForTest == "View", "the message to open");

        await fixture.Dialog.HandleKeyEventAsync(Char('r'));

        Assert.AreEqual("Compose", fixture.Dialog.ModeForTest);
        Assert.AreEqual("ada@example.test", fixture.Dialog.DraftForTest[0]);
        Assert.AreEqual("Re: Welcome", fixture.Dialog.DraftForTest[1]);
    }

    [TestMethod]
    public async Task SetupSavesTheAccountAndItsPasswordToTheVault()
    {
        var fixture = Create(withAccount: false);
        await ShowAsync(fixture.Dialog);

        await TypeAsync(fixture.Dialog, "Home");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await TypeAsync(fixture.Dialog, "pop.example.test");
        for (var i = 0; i < 7; i++)
        {
            await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        }

        await TypeAsync(fixture.Dialog, "pw");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.S, 's', control: true));

        var saved = fixture.Accounts.Accounts.Single();
        Assert.AreEqual("Home", saved.Name);
        Assert.AreEqual("pop.example.test", saved.PopHost);
        fixture.Vault.Verify(v => v.Save(It.Is<PasswordEntry>(e => e.Id == saved.SecretProfileId && e.Password == "pw")), Times.Once);
    }
}
