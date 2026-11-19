using System.Net;
using iConsole.Data;
using iConsole.Prompts;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// 7-SMB, the network browser on the folder selector. The row rules are pinned on <see cref="SmbBrowserRows"/>
/// directly; the page itself is driven through the whole dialog (see <c>FolderSelectorSelectionTest</c> for why
/// a page on its own is not the path the app takes).
/// </summary>
[TestClass]
public class FolderSelectorSmbTabPageTest
{
    private static readonly IPAddress HostA = IPAddress.Parse("192.0.2.10");
    private static readonly IPAddress HostB = IPAddress.Parse("192.0.2.11");

    private static DiscoveredHost Smb(IPAddress address, params string[] shares) =>
        new(address, false, true, shares);

    private static NetworkConnectionProfile SavedSmb(string host, string share, string username) => new()
    {
        Protocol = NetworkProtocol.Smb,
        Host = host,
        ShareName = share,
        Username = username,
        HasSavedCredential = false,
    };

    #region Row rules

    [Timeout(60_000)]
    [TestMethod]
    public void Hosts_ListsOnlyHostsThatAnswerOnSmb()
    {
        var sshOnly = new DiscoveredHost(HostB, true, false, []);

        var rows = SmbBrowserRows.Hosts([Smb(HostA, "Public"), sshOnly], []);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(HostA.ToString(), rows[0].Host);
        Assert.IsNull(rows[0].Share, "A host row names no share.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hosts_SavedSmbConnectionIsListedEvenWhenItWasNotFound()
    {
        var rows = SmbBrowserRows.Hosts([], [SavedSmb("192.0.2.20", "Data", "alice")]);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("192.0.2.20", rows[0].Host);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hosts_FoundHostWithASavedConnectionIsListedOnce()
    {
        var rows = SmbBrowserRows.Hosts([Smb(HostA, "Public")], [SavedSmb(HostA.ToString(), "Data", "alice")]);

        Assert.AreEqual(1, rows.Count);
        StringAssert.Contains(rows[0].Label, "saved");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Shares_ListsTheDiscoveredSharesOfTheHost()
    {
        var rows = SmbBrowserRows.Shares(HostA.ToString(), [Smb(HostA, "Public", "Docs")], []);

        CollectionAssert.AreEqual(new[] { "Public", "Docs" }, rows.Select(r => r.Share).ToArray());
        Assert.IsTrue(rows.All(r => r.Username is null), "A discovered share has no login.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Shares_SavedLoginReplacesTheDiscoveredShareOfTheSameName()
    {
        var rows = SmbBrowserRows.Shares(
            HostA.ToString(),
            [Smb(HostA, "Public", "Docs")],
            [SavedSmb(HostA.ToString(), "docs", "alice")]);

        Assert.AreEqual(2, rows.Count, "The saved Docs replaces the discovered one; Public stays.");
        var docs = rows.Single(r => string.Equals(r.Share, "docs", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("alice", docs.Username);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Shares_HostWithNothingListedHasNoRows()
    {
        var rows = SmbBrowserRows.Shares(HostB.ToString(), [Smb(HostB)], []);

        Assert.AreEqual(0, rows.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UrlFor_IsTheShareAddressWithTheLoginWhenThereIsOne()
    {
        var anonymous = new FolderSelectorSmbTabPage.SmbEntry("192.0.2.10", "Public", null, "label");
        var withLogin = new FolderSelectorSmbTabPage.SmbEntry("192.0.2.20", "Data", "alice", "label");

        Assert.AreEqual("smb://192.0.2.10/Public/", SmbBrowserRows.UrlFor(anonymous));
        Assert.AreEqual("smb://alice@192.0.2.20/Data/", SmbBrowserRows.UrlFor(withLogin));
    }

    #endregion

    [Timeout(60_000)]
    [TestMethod]
    public void Shares_LoginListingAddsItsSharesAndOpensThemWithThatLogin()
    {
        var login = new FolderSelectorSmbTabPage.LoginShares(HostA.ToString(), "alice", ["Private", "Public"]);

        var rows = SmbBrowserRows.Shares(HostA.ToString(), [Smb(HostA, "Public")], [], [login]);

        CollectionAssert.AreEqual(new[] { "Public", "Private" }, rows.Select(r => r.Share).ToArray());
        Assert.IsTrue(rows.All(r => r.Username == "alice"), "A share the login lists opens with that login.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hosts_HostThatOnlyAcceptsTheLoginIsStillListed()
    {
        var login = new FolderSelectorSmbTabPage.LoginShares(HostA.ToString(), "alice", []);

        var rows = SmbBrowserRows.Hosts([], [], [login]);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(HostA.ToString(), rows[0].Host);
        StringAssert.Contains(rows[0].Label, "logged in as alice");
    }

    #region The page, through the dialog

    private sealed record Fixture(
        FolderSelectorTestHarness Harness,
        FolderSelectorTestHarness.TestRoot Root,
        FolderSelectorDialog Dialog,
        FolderSelectorSmbTabPage Page);

    /// <summary>Opens the selector on the 7-SMB page with the given hosts found and saved connections.</summary>
    private static async Task<Fixture> OpenAsync(
        IEnumerable<DiscoveredHost> found,
        IEnumerable<NetworkConnectionProfile> profiles)
    {
        var harness = new FolderSelectorTestHarness();
        harness.NetworkProfileServiceMock.Setup(s => s.GetAll()).Returns(profiles.ToList());
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = new FolderSelectorDialog(
            harness.ContentModelService, harness.FolderHistoryService, harness.DriveInfoModel,
            harness.FolderIndexModel, harness.BeepService, harness.DateTimeProviderService,
            harness.SettingsService, harness.FrequentExecutionGuardService, harness.ThemeService,
            harness.NetworkDiscoveryService, harness.NetworkProfileService, harness.RemoteConnectionService, harness.PasswordManager, harness.PromptService)
        {
            ViewSize = new Size(100, 30)
        };
        await root.ShowDialogAsync(dialog);
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D7, control: true));
        var page = (FolderSelectorSmbTabPage)dialog.TabPages[7];
        page.ApplyFoundForTest(found);
        return new Fixture(harness, root, dialog, page);
    }

    private static Task<bool> EnterAsync(Fixture fixture) =>
        FolderSelectorTestHarness.PressAsync(fixture.Root, FolderSelectorTestHarness.Key(ConsoleKey.Enter));

    private static Task<bool> BackspaceAsync(Fixture fixture) =>
        FolderSelectorTestHarness.PressAsync(fixture.Root, FolderSelectorTestHarness.Key(ConsoleKey.Backspace));

    [Timeout(60_000)]
    [TestMethod]
    public async Task Ctrl7_OpensTheSmbPageWithTheDiscoveredHosts()
    {
        var fixture = await OpenAsync([Smb(HostA, "Public")], []);

        Assert.IsTrue(fixture.Dialog.TabPages[7].IsFocused, "Ctrl+7 should focus the SMB page.");
        CollectionAssert.AreEqual(new[] { HostA.ToString() }, fixture.Page.RowsForTest.Select(r => r.Host).ToArray());
        Assert.IsNull(fixture.Page.HostFilterForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnAHost_ListsItsSharesAndBackspaceReturnsToTheHosts()
    {
        var fixture = await OpenAsync([Smb(HostA, "Public", "Docs")], []);

        await EnterAsync(fixture);

        Assert.AreEqual(HostA.ToString(), fixture.Page.HostFilterForTest);
        Assert.AreEqual(2, fixture.Page.RowsForTest.Count);
        Assert.IsTrue(fixture.Page.RowsForTest.All(r => r.Share is not null));
        Assert.IsTrue(fixture.Dialog.IsVisible, "Opening a host does not close the selector.");
        Assert.AreEqual(0, fixture.Harness.Navigations.Count);

        await BackspaceAsync(fixture);

        Assert.IsNull(fixture.Page.HostFilterForTest);
        Assert.AreEqual(1, fixture.Page.RowsForTest.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnAShare_GoesToItAndClosesTheSelector()
    {
        var fixture = await OpenAsync([Smb(HostA, "Public")], []);
        await EnterAsync(fixture);

        await EnterAsync(fixture);

        CollectionAssert.AreEqual(new[] { "smb://192.0.2.10/Public/" }, fixture.Harness.Navigations);
        Assert.IsFalse(fixture.Dialog.IsVisible, "The selector should close itself once a share has been chosen.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnAHostWithNoShares_SaysWhyAndStays()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);

        await EnterAsync(fixture);

        Assert.IsNull(fixture.Page.HostFilterForTest, "A host with no shares listed is not opened.");
        Assert.AreEqual(0, fixture.Harness.Navigations.Count);
        Assert.IsTrue(fixture.Dialog.IsVisible);
        StringAssert.Contains(fixture.Page.StatusForTest, "without a login");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnASavedShare_GoesToItWithTheSavedLogin()
    {
        var fixture = await OpenAsync([], [SavedSmb("192.0.2.20", "Data", "alice")]);
        await EnterAsync(fixture);

        await EnterAsync(fixture);

        CollectionAssert.AreEqual(new[] { "smb://alice@192.0.2.20/Data/" }, fixture.Harness.Navigations);
        Assert.IsFalse(fixture.Dialog.IsVisible);
    }

    #endregion

    #region Log in

    private static readonly IReadOnlyList<string> PrivateAndPublic = ["Private", "Public"];

    /// <summary>The answers the username and password prompts give, in the order they are asked. A null is a cancel.</summary>
    private static void AnswerPrompts(Fixture fixture, params string?[] answers)
    {
        var sequence = fixture.Harness.PromptServiceMock.SetupSequence(p => p.AskAsync<string>(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>()));
        foreach (var answer in answers)
        {
            sequence.Returns(Task.FromResult<string?>(answer));
        }
    }

    private static void ListsSharesWithLogin(Fixture fixture, string username, string password, IReadOnlyList<string>? shares, int smbVersion = 3) =>
        fixture.Harness.NetworkDiscoveryServiceMock
            .Setup(s => s.ListSharesWithLoginAsync(HostB.ToString(), username, password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shares is null ? null : new SmbListing(shares, smbVersion));

    [Timeout(60_000)]
    [TestMethod]
    public async Task Login_ListsTheSharesWithTheTypedLoginAndSavesItInThePasswordSafe()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);
        fixture.Harness.PasswordManagerMock.SetupGet(p => p.IsUnlocked).Returns(true);
        AnswerPrompts(fixture, "alice", "pw-a");
        ListsSharesWithLogin(fixture, "alice", "pw-a", PrivateAndPublic);

        await fixture.Page.LoginForTestAsync();
        fixture.Page.OnIdle();

        Assert.AreEqual(HostB.ToString(), fixture.Page.HostFilterForTest, "The list opens on the host that was logged in to.");
        CollectionAssert.AreEqual(new[] { "Private", "Public" }, fixture.Page.RowsForTest.Select(r => r.Share).ToArray());
        Assert.IsTrue(fixture.Page.RowsForTest.All(r => r.Username == "alice"));
        fixture.Harness.PasswordManagerMock.Verify(p => p.Save(It.Is<PasswordEntry>(e =>
            e.Id == SmbLoginKey.Id(HostB.ToString(), "alice")
            && e.Username == "alice"
            && e.Password == "pw-a"
            && e.Category == PasswordCategory.Network)), Times.Once);
        StringAssert.Contains(fixture.Page.StatusForTest, "saved in the password safe");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Login_ThenEnterOnAShareOpensItAsThatLogin()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);
        fixture.Harness.PasswordManagerMock.SetupGet(p => p.IsUnlocked).Returns(true);
        AnswerPrompts(fixture, "alice", "pw-a");
        ListsSharesWithLogin(fixture, "alice", "pw-a", PrivateAndPublic);
        await fixture.Page.LoginForTestAsync();
        fixture.Page.OnIdle();

        await EnterAsync(fixture);

        CollectionAssert.AreEqual(new[] { "smb://alice@192.0.2.11/Private/" }, fixture.Harness.Navigations);
        Assert.IsFalse(fixture.Dialog.IsVisible, "The selector closes once a share is chosen.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Login_Refused_SavesNothingAndSaysSo()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);
        fixture.Harness.PasswordManagerMock.SetupGet(p => p.IsUnlocked).Returns(true);
        AnswerPrompts(fixture, "alice", "wrong");
        ListsSharesWithLogin(fixture, "alice", "wrong", null);

        await fixture.Page.LoginForTestAsync();
        fixture.Page.OnIdle();

        Assert.IsNull(fixture.Page.HostFilterForTest);
        Assert.AreEqual(0, fixture.Page.LoginsForTest.Count);
        fixture.Harness.PasswordManagerMock.Verify(p => p.Save(It.IsAny<PasswordEntry>()), Times.Never);
        StringAssert.Contains(fixture.Page.StatusForTest, "Nothing was saved");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Login_WhenThePasswordSafeCannotBeOpened_ListsTheSharesButSaysTheLoginWasNotKept()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);
        fixture.Harness.PasswordManagerMock.SetupGet(p => p.IsUnlocked).Returns(false);
        fixture.Harness.PasswordManagerMock.Setup(p => p.Unlock()).Returns(false);
        AnswerPrompts(fixture, "alice", "pw-a");
        ListsSharesWithLogin(fixture, "alice", "pw-a", PrivateAndPublic);

        await fixture.Page.LoginForTestAsync();
        fixture.Page.OnIdle();

        Assert.AreEqual(HostB.ToString(), fixture.Page.HostFilterForTest);
        fixture.Harness.PasswordManagerMock.Verify(p => p.Save(It.IsAny<PasswordEntry>()), Times.Never);
        StringAssert.Contains(fixture.Page.StatusForTest, "could not be opened");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Login_CancelledAtTheUsernamePrompt_AsksNoPasswordAndListsNothing()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);
        AnswerPrompts(fixture, (string?)null);

        await fixture.Page.LoginForTestAsync();

        fixture.Harness.PromptServiceMock.Verify(p => p.AskAsync<string>(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Harness.NetworkDiscoveryServiceMock.Verify(s => s.ListSharesWithLoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task L_OnAHostAsksForTheUsernameOfThatHost()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);
        AnswerPrompts(fixture, (string?)null);

        await FolderSelectorTestHarness.PressAsync(fixture.Root, new KeyEvent('l', ConsoleKey.L, false, false, false));

        fixture.Harness.PromptServiceMock.Verify(p => p.AskAsync<string>(
            It.Is<TextPrompt<string>>(prompt => prompt.Question.Contains(HostB.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SignedInLine_ShowsTheSmbVersionTheHostWasListedWith()
    {
        var fixture = await OpenAsync([new DiscoveredHost(HostB, false, true, [], 2)], []);

        Assert.AreEqual("Browsing 192.0.2.11 anonymously. L logs in.  SMB 2 in use.", fixture.Page.SignedInForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SignedInLine_HasNoVersionHintForAHostWhoseVersionIsNotKnown()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);

        Assert.AreEqual("Browsing 192.0.2.11 anonymously. L logs in.", fixture.Page.SignedInForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SignedInLine_NamesTheUserOfTheHostUnderTheCursor()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);
        Assert.AreEqual("Browsing 192.0.2.11 anonymously. L logs in.", fixture.Page.SignedInForTest);
        fixture.Harness.PasswordManagerMock.SetupGet(p => p.IsUnlocked).Returns(true);
        AnswerPrompts(fixture, "alice", "pw-a");
        ListsSharesWithLogin(fixture, "alice", "pw-a", PrivateAndPublic);

        await fixture.Page.LoginForTestAsync();
        fixture.Page.OnIdle();

        Assert.AreEqual("Logged in to 192.0.2.11 as alice.  SMB 3 in use.", fixture.Page.SignedInForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Logout_AsksFirstThenRemovesTheLoginAndItsSavedPassword()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);
        fixture.Harness.PasswordManagerMock.SetupGet(p => p.IsUnlocked).Returns(true);
        AnswerPrompts(fixture, "alice", "pw-a");
        ListsSharesWithLogin(fixture, "alice", "pw-a", PrivateAndPublic);
        await fixture.Page.LoginForTestAsync();
        fixture.Page.OnIdle();

        await FolderSelectorTestHarness.PressAsync(fixture.Root, new KeyEvent('o', ConsoleKey.O, false, false, false));
        fixture.Page.OnIdle();

        StringAssert.Contains(fixture.Page.StatusForTest, "y:yes");
        Assert.AreEqual(HostB.ToString(), fixture.Page.PendingLogoutHostForTest);
        fixture.Harness.PasswordManagerMock.Verify(p => p.Delete(It.IsAny<string>()), Times.Never);

        await FolderSelectorTestHarness.PressAsync(fixture.Root, new KeyEvent('y', ConsoleKey.Y, false, false, false));
        fixture.Page.OnIdle();

        fixture.Harness.PasswordManagerMock.Verify(p => p.Delete(SmbLoginKey.Id(HostB.ToString(), "alice")), Times.Once);
        Assert.AreEqual(0, fixture.Page.LoginsForTest.Count);
        Assert.IsNull(fixture.Page.HostFilterForTest, "A host with no shares left to show goes back to the list of hosts.");
        StringAssert.Contains(fixture.Page.StatusForTest, "Logged out of");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Logout_AnswerNo_KeepsTheLogin()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);
        fixture.Harness.PasswordManagerMock.SetupGet(p => p.IsUnlocked).Returns(true);
        AnswerPrompts(fixture, "alice", "pw-a");
        ListsSharesWithLogin(fixture, "alice", "pw-a", PrivateAndPublic);
        await fixture.Page.LoginForTestAsync();
        fixture.Page.OnIdle();

        await FolderSelectorTestHarness.PressAsync(fixture.Root, new KeyEvent('o', ConsoleKey.O, false, false, false));
        await FolderSelectorTestHarness.PressAsync(fixture.Root, new KeyEvent('n', ConsoleKey.N, false, false, false));
        fixture.Page.OnIdle();

        Assert.IsNull(fixture.Page.PendingLogoutHostForTest);
        Assert.AreEqual(1, fixture.Page.LoginsForTest.Count);
        fixture.Harness.PasswordManagerMock.Verify(p => p.Delete(It.IsAny<string>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Logout_OnAHostWithoutALogin_SaysSo()
    {
        var fixture = await OpenAsync([Smb(HostB)], []);

        await FolderSelectorTestHarness.PressAsync(fixture.Root, new KeyEvent('o', ConsoleKey.O, false, false, false));
        fixture.Page.OnIdle();

        Assert.IsNull(fixture.Page.PendingLogoutHostForTest);
        StringAssert.Contains(fixture.Page.StatusForTest, "Not logged in to 192.0.2.11.");
    }

    #endregion
}
