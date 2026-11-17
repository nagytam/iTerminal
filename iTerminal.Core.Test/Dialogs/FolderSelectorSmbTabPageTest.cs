using System.Net;
using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;

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
            harness.NetworkDiscoveryService, harness.NetworkProfileService)
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
}
