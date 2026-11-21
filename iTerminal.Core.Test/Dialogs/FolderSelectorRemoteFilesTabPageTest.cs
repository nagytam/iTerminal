using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Model.Remote;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// 8-FTP/SFTP, the saved FTP and SFTP connections on the folder selector. The row rules are pinned on
/// <see cref="RemoteFilesBrowserRows"/> directly; the page is driven through the whole dialog, as the 7-SMB
/// tests are, so that Ctrl+8, the list and Enter are the path the app takes.
/// </summary>
[TestClass]
public class FolderSelectorRemoteFilesTabPageTest
{
    private static NetworkConnectionProfile Ftp(string host, string username = "", string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Protocol = NetworkProtocol.Ftp,
        Host = host,
        Username = username,
    };

    private static NetworkConnectionProfile Sftp(string host, string username = "", string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Protocol = NetworkProtocol.Ssh,
        Host = host,
        Username = username,
    };

    private static RemoteConnection FtpEntry(string id, string host, string username, string remotePath) => new()
    {
        Id = id,
        Name = host,
        Protocol = RemoteProtocol.Ftp,
        Host = host,
        Username = username,
        RemotePath = remotePath,
    };

    #region Row rules

    [Timeout(60_000)]
    [TestMethod]
    public void Build_FtpProfileWithASavedEntry_OpensAtTheFolderItWasSavedWith()
    {
        var profile = Ftp("ftp.example.com", "ann", "ftp-1");
        var entry = FtpEntry("ftp-1", "ftp.example.com", "ann", "/pub/files/");

        var rows = RemoteFilesBrowserRows.Build([profile], [entry]);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(Container.FTP + "ann@ftp.example.com/pub/files/", rows[0].Url);
        Assert.AreEqual("FTP   ann@ftp.example.com/pub/files/", rows[0].Label);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_FtpProfileWithoutASavedEntry_OpensAtTheRoot()
    {
        var rows = RemoteFilesBrowserRows.Build([Ftp("ftp.example.com", "ann")], []);

        Assert.AreEqual(Container.FTP + "ann@ftp.example.com/", rows[0].Url);
        Assert.AreEqual("FTP   ann@ftp.example.com/", rows[0].Label);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_AnonymousFtpProfile_HasNoUserPart()
    {
        var rows = RemoteFilesBrowserRows.Build([Ftp("ftp.example.com")], []);

        Assert.AreEqual(Container.FTP + "ftp.example.com/", rows[0].Url);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_SftpProfile_OpensAtTheRootInTheSshForm()
    {
        var rows = RemoteFilesBrowserRows.Build([Sftp("files.example.com", "tamas")], []);

        Assert.AreEqual(Container.SSH + "tamas@files.example.com:/", rows[0].Url);
        Assert.AreEqual("SFTP  tamas@files.example.com/", rows[0].Label);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_LeavesOutTheProtocolsThatAreNotFileConnections()
    {
        var smb = new NetworkConnectionProfile { Protocol = NetworkProtocol.Smb, Host = "192.0.2.10", ShareName = "Public" };
        var webDav = new NetworkConnectionProfile { Protocol = NetworkProtocol.WebDav, Host = "dav.example.com" };

        var rows = RemoteFilesBrowserRows.Build([smb, webDav, Sftp("files.example.com", "tamas")], []);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(Container.SSH + "tamas@files.example.com:/", rows[0].Url);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_SortsByHostThenUser()
    {
        var rows = RemoteFilesBrowserRows.Build(
            [Sftp("b.example.com", "amy"), Ftp("a.example.com", "zed"), Sftp("a.example.com", "bob")],
            []);

        CollectionAssert.AreEqual(
            new[] { "SFTP  bob@a.example.com/", "FTP   zed@a.example.com/", "SFTP  amy@b.example.com/" },
            rows.Select(r => r.Label).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Build_AnSftpProfileDoesNotTakeAFolderFromAnEntryWithTheSameId()
    {
        // Only FTP entries carry a folder; a shell entry sharing the id is not a file connection to take one from.
        var profile = Sftp("files.example.com", "tamas", "same");
        var shell = new RemoteConnection { Id = "same", Protocol = RemoteProtocol.Ssh, Host = "files.example.com", Username = "tamas", RemotePath = "/srv" };

        var rows = RemoteFilesBrowserRows.Build([profile], [shell]);

        Assert.AreEqual(Container.SSH + "tamas@files.example.com:/", rows[0].Url);
    }

    #endregion

    #region The page, through the dialog

    [Timeout(60_000)]
    [TestMethod]
    public async Task Ctrl8_ShowsTheSavedConnectionsPage()
    {
        var harness = new FolderSelectorTestHarness();
        harness.NetworkProfileServiceMock.Setup(s => s.GetAll()).Returns([Sftp("files.example.com", "tamas")]);
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D8, control: true));

        Assert.AreEqual(9, dialog.TabPages.Count, "The selector has nine pages, 0 to 8.");
        Assert.IsInstanceOfType<FolderSelectorRemoteFilesTabPage>(dialog.TabPages[8]);
        Assert.AreEqual("8-FTP/SFTP", dialog.TabPages[8].Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnASavedSftpConnection_GoesToItsRootAndCloses()
    {
        var harness = new FolderSelectorTestHarness();
        harness.NetworkProfileServiceMock.Setup(s => s.GetAll()).Returns([Sftp("files.example.com", "tamas")]);
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D8, control: true));

        var handled = await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { new Url(Container.SSH + "tamas@files.example.com:/").FullUrl }, harness.Navigations);
        Assert.IsFalse(dialog.IsVisible, "The selector should close itself once a folder has been chosen.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnASavedFtpConnection_GoesToItsFolderAndCloses()
    {
        var harness = new FolderSelectorTestHarness();
        harness.NetworkProfileServiceMock.Setup(s => s.GetAll()).Returns([Ftp("ftp.example.com", "ann", "ftp-1")]);
        harness.RemoteConnectionServiceMock.Setup(s => s.GetAll()).Returns([FtpEntry("ftp-1", "ftp.example.com", "ann", "pub/files")]);
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D8, control: true));

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { new Url(Container.FTP + "ann@ftp.example.com/pub/files").FullUrl }, harness.Navigations);
        Assert.IsFalse(dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Page_SavesAnSshEntryThatHasNoProfileYet_Once()
    {
        // An SSH entry saved before the page listed it: opening the page writes its profile.
        var harness = new FolderSelectorTestHarness();
        var entry = new RemoteConnection { Id = "old", Name = "web", Protocol = RemoteProtocol.Ssh, Host = "files.example.com", Username = "tamas" };
        var profiles = new List<NetworkConnectionProfile>();
        harness.RemoteConnectionServiceMock.Setup(s => s.GetAll()).Returns([entry]);
        harness.NetworkProfileServiceMock.Setup(s => s.GetAll()).Returns(() => profiles);
        // The real service writes the profile when the entry is saved; the mock does the same, so a second load sees it.
        harness.RemoteConnectionServiceMock
            .Setup(s => s.Save(It.IsAny<RemoteConnection>()))
            .Callback<RemoteConnection>(c => profiles.Add(new NetworkConnectionProfile { Id = c.Id, Protocol = NetworkProtocol.Ssh, Host = c.Host, Username = c.Username }));
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);

        await root.ShowDialogAsync(dialog);

        harness.RemoteConnectionServiceMock.Verify(s => s.Save(It.Is<RemoteConnection>(c => c.Id == "old")), Moq.Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Page_WithNoSavedConnections_HasNothingToOpen()
    {
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D8, control: true));

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(Array.Empty<string>(), harness.Navigations);
        Assert.IsTrue(dialog.IsVisible, "With nothing listed there is nowhere to go, so the selector stays up.");
    }

    #region Adding a server

    private static FolderSelectorAddServerDialog FindForm(FolderSelectorTestHarness.TestRoot root) =>
        root.Controls.OfType<FolderSelectorAddServerDialog>().Single();

    /// <summary>Selector up on the 8-FTP/SFTP page, with N pressed: the add-a-server form is over it.</summary>
    private static async Task<(FolderSelectorTestHarness.TestRoot Root, FolderSelectorDialog Dialog, FolderSelectorAddServerDialog Form)> OpenForm(FolderSelectorTestHarness harness)
    {
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D8, control: true));
        await FolderSelectorTestHarness.PressAsync(root, new KeyEvent('n', ConsoleKey.N, false, false, false));
        return (root, dialog, FindForm(root));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task N_OnTheRemoteFilesPage_OpensTheAddServerForm()
    {
        var harness = new FolderSelectorTestHarness();
        var (root, dialog, form) = await OpenForm(harness);

        Assert.IsTrue(form.IsVisible);
        Assert.IsTrue(dialog.IsVisible, "The form is over the selector; the selector stays up behind it.");
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AddingAnSftpServer_SavesItWithItsPasswordAndOpensItsRoot()
    {
        var harness = new FolderSelectorTestHarness();
        var (root, dialog, form) = await OpenForm(harness);
        form.ProtocolForTest.SelectedIndex = 1;
        form.HostForTest.Text = "files.example.com";
        form.UserForTest.Text = "tamas";
        form.PasswordForTest.Text = "s3cret";

        var added = await form.AddAsync();

        Assert.IsTrue(added);
        harness.RemoteConnectionServiceMock.Verify(
            s => s.Save(
                It.Is<RemoteConnection>(c => c.Protocol == RemoteProtocol.Ssh && c.Host == "files.example.com" && c.Username == "tamas" && c.Name == "files.example.com"),
                "s3cret"),
            Moq.Times.Once);
        harness.RemoteConnectionServiceMock.Verify(s => s.MarkConnected(It.IsAny<string>()), Moq.Times.Once);
        CollectionAssert.AreEqual(new[] { new Url(Container.SSH + "tamas@files.example.com:/").FullUrl }, harness.Navigations);
        Assert.IsFalse(dialog.IsVisible, "The selector closes once the panel is on the new server.");
        Assert.IsFalse(form.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AddingAnFtpServer_SavesItAndOpensTheFolderTyped()
    {
        var harness = new FolderSelectorTestHarness();
        var (root, dialog, form) = await OpenForm(harness);
        form.HostForTest.Text = "ftp.example.com";
        form.PortForTest.Text = "2121";
        form.UserForTest.Text = "ann";
        form.FolderForTest.Text = "pub/files";

        await form.AddAsync();

        harness.RemoteConnectionServiceMock.Verify(
            s => s.Save(It.Is<RemoteConnection>(c => c.Protocol == RemoteProtocol.Ftp && c.Port == 2121 && c.RemotePath == "pub/files"), null),
            Moq.Times.Once);
        CollectionAssert.AreEqual(new[] { new Url(Container.FTP + "ann@ftp.example.com:2121/pub/files").FullUrl }, harness.Navigations);
        Assert.IsFalse(dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnSftpServer_HasNoPortOrFolderToType()
    {
        var harness = new FolderSelectorTestHarness();
        var (root, dialog, form) = await OpenForm(harness);
        Assert.IsTrue(form.PortForTest.IsEnabled, "FTP is the default type, so its port is open.");

        form.ProtocolForTest.SelectedIndex = 1;

        Assert.IsFalse(form.PortForTest.IsEnabled, "The SSH drive connects on port 22 only.");
        Assert.IsFalse(form.FolderForTest.IsEnabled, "An SFTP server opens at its root.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AddingWithoutAHost_StaysOpenWithAMessage()
    {
        var harness = new FolderSelectorTestHarness();
        var (root, dialog, form) = await OpenForm(harness);

        var added = await form.AddAsync();

        Assert.IsFalse(added);
        Assert.IsFalse(string.IsNullOrEmpty(form.ErrorForTest));
        Assert.IsTrue(form.IsVisible);
        harness.RemoteConnectionServiceMock.Verify(s => s.Save(It.IsAny<RemoteConnection>(), It.IsAny<string?>()), Moq.Times.Never);
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInvalidPort_IsRefusedAndNothingIsSaved()
    {
        var harness = new FolderSelectorTestHarness();
        var (root, dialog, form) = await OpenForm(harness);
        form.HostForTest.Text = "ftp.example.com";
        form.PortForTest.Text = "70000";

        Assert.IsFalse(await form.AddAsync());

        Assert.IsTrue(form.ErrorForTest.Contains("65535"), form.ErrorForTest);
        harness.RemoteConnectionServiceMock.Verify(s => s.Save(It.IsAny<RemoteConnection>(), It.IsAny<string?>()), Moq.Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALockedPasswordManager_KeepsTheFormOpenWithTheReason()
    {
        var harness = new FolderSelectorTestHarness();
        harness.RemoteConnectionServiceMock
            .Setup(s => s.Save(It.IsAny<RemoteConnection>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("The password manager could not be opened, so the password was not saved."));
        var (root, dialog, form) = await OpenForm(harness);
        form.HostForTest.Text = "ftp.example.com";
        form.PasswordForTest.Text = "s3cret";

        Assert.IsFalse(await form.AddAsync());

        Assert.IsTrue(form.ErrorForTest.Contains("password manager"), form.ErrorForTest);
        Assert.IsTrue(form.IsVisible);
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterInTheForm_AddsTheServer()
    {
        var harness = new FolderSelectorTestHarness();
        var (root, dialog, form) = await OpenForm(harness);
        form.HostForTest.Text = "files.example.com";

        // Enter is the form's own key, whichever control holds the cursor. FTP is the type the form starts on.
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsFalse(form.IsVisible);
        CollectionAssert.AreEqual(new[] { new Url(Container.FTP + "files.example.com/").FullUrl }, harness.Navigations);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Esc_ClosesOnlyTheForm()
    {
        var harness = new FolderSelectorTestHarness();
        var (root, dialog, form) = await OpenForm(harness);

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Escape));

        Assert.IsFalse(form.IsVisible);
        Assert.IsTrue(dialog.IsVisible, "One Esc closes one layer: the selector is still up.");
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    #endregion

    private static FolderSelectorDialog CreateDialog(FolderSelectorTestHarness harness) =>
        new(
            harness.ContentModelService, harness.FolderHistoryService, harness.DriveInfoModel,
            harness.FolderIndexModel, harness.BeepService, harness.DateTimeProviderService,
            harness.SettingsService, harness.FrequentExecutionGuardService, harness.ThemeService,
            harness.NetworkDiscoveryService, harness.NetworkProfileService, harness.RemoteConnectionService,
            harness.PasswordManager, harness.PromptService)
        {
            ViewSize = new Size(100, 30)
        };

    #endregion
}
