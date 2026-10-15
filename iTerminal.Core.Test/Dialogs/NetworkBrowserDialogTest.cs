using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;
using Moq;
using System.Net;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class NetworkBrowserDialogTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static async IAsyncEnumerable<DiscoveredHost> Hosts(params DiscoveredHost[] hosts)
    {
        foreach (var host in hosts)
        {
            yield return host;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<DiscoveredHost> EmptyHosts()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static (
        NetworkBrowserDialog Dialog,
        Mock<INetworkDiscoveryService> Discovery,
        Mock<INetworkProfileService> Profiles,
        Mock<IPasswordManager> Vault,
        Mock<IContentModelService> ContentModels
        ) CreateDialog(IEnumerable<DiscoveredHost>? scanResults = null, IEnumerable<NetworkConnectionProfile>? savedProfiles = null)
    {
        var themeService = CreateThemeService();
        var discovery = new Mock<INetworkDiscoveryService>();
        discovery.Setup(s => s.ScanAsync(It.IsAny<CancellationToken>()))
            .Returns(scanResults is null ? EmptyHosts() : Hosts([.. scanResults]));
        var profiles = new Mock<INetworkProfileService>();
        profiles.Setup(s => s.GetAll()).Returns([.. savedProfiles ?? []]);
        var vault = new Mock<IPasswordManager>();
        var contentModels = new Mock<IContentModelService>();

        var dialog = new NetworkBrowserDialog(
            discovery.Object,
            profiles.Object,
            vault.Object,
            contentModels.Object,
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            themeService);

        return (dialog, discovery, profiles, vault, contentModels);
    }

    private static async Task<Control> ShowAsync(NetworkBrowserDialog dialog)
    {
        var themeService = CreateThemeService();
        var root = new Control(themeService) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);
        return root;
    }

    /// <summary>Polls (rather than a fixed delay) until the background scan task has populated
    /// <see cref="NetworkBrowserDialog.HostRowsForTest"/> to satisfy <paramref name="predicate"/>
    /// and the host list box lists them too,
    /// so these tests stay reliable under heavy parallel test-run load instead of racing a fixed
    /// <c>Task.Delay</c> against thread-pool contention.</summary>
    private static async Task WaitForRowsAsync(
        NetworkBrowserDialog dialog,
        Func<IReadOnlyList<NetworkBrowserDialog.HostRow>, bool> predicate,
        int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            // The list box projects the rows on its own schedule; a row picked by index before it
            // is listed there is not there to pick.
            var rows = dialog.HostRowsForTest;
            if (predicate(rows) && dialog.ListedRowCountForTest == rows.Count)
            {
                return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for the expected host rows to appear.");
    }

    [TestMethod]
    public async Task OnInitializeAsync_LoadsSavedProfiles()
    {
        var profile = new NetworkConnectionProfile { Protocol = NetworkProtocol.Ssh, Host = "host1", Username = "alice" };
        var (dialog, _, _, _, _) = CreateDialog(savedProfiles: [profile]);

        await ShowAsync(dialog);

        var rows = dialog.HostRowsForTest;
        Assert.IsTrue(rows.Any(r => r.Kind == NetworkBrowserDialog.HostRowKind.SavedProfile && r.Host == "host1"));
    }

    [TestMethod]
    public async Task OnInitializeAsync_StreamsDiscoveredSshHost()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.5"), SshAvailable: true, SmbAvailable: false, Shares: []);
        var (dialog, _, _, _, _) = CreateDialog(scanResults: [host]);

        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Any(r => r.Kind == NetworkBrowserDialog.HostRowKind.DiscoveredSsh));

        var rows = dialog.HostRowsForTest;
        Assert.IsTrue(rows.Any(r => r.Kind == NetworkBrowserDialog.HostRowKind.DiscoveredSsh && r.Host == "10.0.0.5"));
    }

    [TestMethod]
    public async Task OnInitializeAsync_SmbHostWithShares_AddsOneRowPerShare()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.6"), SshAvailable: false, SmbAvailable: true, Shares: ["Public", "Users"]);
        var (dialog, _, _, _, _) = CreateDialog(scanResults: [host]);

        await ShowAsync(dialog);
        // A host's share rows are added one at a time; wait for both rather than the first.
        await WaitForRowsAsync(dialog, rows => rows.Count(r => r.Kind == NetworkBrowserDialog.HostRowKind.DiscoveredSmbShare) >= 2);

        var rows = dialog.HostRowsForTest.Where(r => r.Kind == NetworkBrowserDialog.HostRowKind.DiscoveredSmbShare).ToList();
        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows.Any(r => r.ShareName == "Public"));
        Assert.IsTrue(rows.Any(r => r.ShareName == "Users"));
    }

    [TestMethod]
    public async Task OnInitializeAsync_SmbHostWithoutShares_AddsLoginRequiredRow()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.7"), SshAvailable: false, SmbAvailable: true, Shares: []);
        var (dialog, _, _, _, _) = CreateDialog(scanResults: [host]);

        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Any(r => r.Kind == NetworkBrowserDialog.HostRowKind.DiscoveredSmbHost));

        var rows = dialog.HostRowsForTest;
        Assert.IsTrue(rows.Any(r => r.Kind == NetworkBrowserDialog.HostRowKind.DiscoveredSmbHost && r.Host == "10.0.0.7"));
    }

    [TestMethod]
    public async Task Rescan_TriggersSecondScan()
    {
        var (dialog, discovery, _, _, _) = CreateDialog();
        var scanCallCount = 0;
        discovery.Setup(s => s.ScanAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref scanCallCount))
            .Returns(EmptyHosts());

        await ShowAsync(dialog);
        await WaitUntilAsync(() => Interlocked.CompareExchange(ref scanCallCount, 0, 0) >= 1);

        await dialog.HandleKeyEventAsync(new KeyEvent('r', ConsoleKey.R, false, false, false));
        await WaitUntilAsync(() => Interlocked.CompareExchange(ref scanCallCount, 0, 0) >= 2);

        discovery.Verify(s => s.ScanAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    /// <summary>Polls (rather than a fixed delay) until <paramref name="condition"/> is true, so
    /// tests that synchronize with a background <c>Task.Run</c> stay reliable under heavy
    /// parallel test-run load instead of racing a fixed <c>Task.Delay</c>.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for the expected condition.");
    }

    [TestMethod]
    public async Task DeleteSelectedProfile_RemovesFromProfileServiceAndVault()
    {
        var profile = new NetworkConnectionProfile { Protocol = NetworkProtocol.Ssh, Host = "host1", Username = "alice" };
        var (dialog, _, profiles, vault, _) = CreateDialog(savedProfiles: [profile]);
        await ShowAsync(dialog);
        dialog.SelectRowForTest(0);

        await dialog.HandleKeyEventAsync(new KeyEvent('d', ConsoleKey.D, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent('y', ConsoleKey.Y, false, false, false));

        profiles.Verify(s => s.Delete(profile.Id), Times.Once);
        vault.Verify(s => s.DeleteSecret(profile.Id), Times.Once);
        Assert.IsFalse(dialog.HostRowsForTest.Any(r => r.Profile == profile));
    }

    [TestMethod]
    public async Task SelectAsync_SavedProfile_ConnectsImmediatelyAndCloses()
    {
        var profile = new NetworkConnectionProfile { Protocol = NetworkProtocol.Ssh, Host = "host1", Username = "alice" };
        var (dialog, _, _, _, contentModels) = CreateDialog(savedProfiles: [profile]);
        var root = await ShowAsync(dialog);
        dialog.SelectRowForTest(0);

        await dialog.SelectAsyncForTest();

        contentModels.Verify(s => s.SetUrlAsync(It.Is<Url>(u => u.FullUrl == "ssh://alice@host1:/")), Times.Once);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog), "dialog should close after connecting");
    }

    [TestMethod]
    public async Task SelectAsync_DiscoveredSshHost_EntersConnectMode()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.5"), SshAvailable: true, SmbAvailable: false, Shares: []);
        var (dialog, _, _, _, _) = CreateDialog(scanResults: [host]);
        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);

        await dialog.SelectAsyncForTest();

        Assert.AreEqual(NetworkBrowserDialog.Mode.Connect, dialog.ModeForTest);
        Assert.IsTrue(dialog.UsernameTextBoxForTest.IsVisible);
    }

    [TestMethod]
    public async Task ConnectPendingAsync_SshHostWithCredentials_ConnectsAndCloses()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.5"), SshAvailable: true, SmbAvailable: false, Shares: []);
        var (dialog, _, _, _, contentModels) = CreateDialog(scanResults: [host]);
        var root = await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);
        await dialog.SelectAsyncForTest();
        dialog.UsernameTextBoxForTest.Text = "bob";
        dialog.PasswordTextBoxForTest.Text = "hunter2";

        await dialog.ConnectPendingAsyncForTest();

        contentModels.Verify(s => s.SetUrlAsync(It.Is<Url>(u => u.FullUrl == "ssh://bob@10.0.0.5:/")), Times.Once);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public async Task ConnectPendingAsync_SmbWithoutShareName_ShowsErrorAndDoesNotConnect()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.7"), SshAvailable: false, SmbAvailable: true, Shares: []);
        var (dialog, _, _, _, contentModels) = CreateDialog(scanResults: [host]);
        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);
        await dialog.SelectAsyncForTest();
        dialog.ShareTextBoxForTest.Text = string.Empty;

        await dialog.ConnectPendingAsyncForTest();

        contentModels.Verify(s => s.SetUrlAsync(It.IsAny<Url>()), Times.Never);
    }

    [TestMethod]
    public async Task ConnectPendingAsync_WithSaveConnectionChecked_SavesProfileAndCredential()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.5"), SshAvailable: true, SmbAvailable: false, Shares: []);
        var (dialog, _, profiles, vault, _) = CreateDialog(scanResults: [host]);
        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);
        await dialog.SelectAsyncForTest();
        dialog.UsernameTextBoxForTest.Text = "bob";
        dialog.PasswordTextBoxForTest.Text = "hunter2";
        dialog.SaveConnectionCheckBoxForTest.IsChecked = true;

        await dialog.ConnectPendingAsyncForTest();

        profiles.Verify(s => s.Save(It.Is<NetworkConnectionProfile>(p => p.Host == "10.0.0.5" && p.Username == "bob")), Times.Once);
        vault.Verify(s => s.Save(It.Is<iTerminal.Core.Model.Passwords.PasswordEntry>(e => e.Password == "hunter2" && e.Category == iTerminal.Core.Model.Passwords.PasswordCategory.Network)), Times.Once);
    }

    [TestMethod]
    public async Task Escape_InConnectMode_ReturnsToBrowseWithoutClosing()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.5"), SshAvailable: true, SmbAvailable: false, Shares: []);
        var (dialog, _, _, _, _) = CreateDialog(scanResults: [host]);
        var root = await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);
        await dialog.SelectAsyncForTest();
        Assert.AreEqual(NetworkBrowserDialog.Mode.Connect, dialog.ModeForTest);

        var handled = await dialog.HandleKeyEventAsync(new KeyEvent('', ConsoleKey.Escape, false, false, false));

        Assert.IsTrue(handled);
        Assert.AreEqual(NetworkBrowserDialog.Mode.Browse, dialog.ModeForTest);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "Esc should not close the dialog while backing out of the connect form");
    }

    [TestMethod]
    public async Task Escape_InBrowseMode_ClosesDialog()
    {
        var (dialog, _, _, _, _) = CreateDialog();
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('', ConsoleKey.Escape, false, false, false));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public async Task OpeningShowsOnlyTheHostListNotTheConnectForm()
    {
        // Control.ShowAsync makes every child visible after OnInitializeAsync, so the connect form
        // the dialog hid there used to sit on top of the host list from the moment it opened.
        var (dialog, _, _, _, _) = CreateDialog();

        await ShowAsync(dialog);

        Assert.AreEqual(NetworkBrowserDialog.Mode.Browse, dialog.ModeForTest);
        Assert.IsFalse(dialog.UsernameTextBoxForTest.IsVisible);
        Assert.IsFalse(dialog.PasswordTextBoxForTest.IsVisible);
        Assert.IsFalse(dialog.ShareTextBoxForTest.IsVisible);
        Assert.IsFalse(dialog.SaveConnectionCheckBoxForTest.IsVisible);
    }

    private static async IAsyncEnumerable<DiscoveredHost> FailingScan()
    {
        await Task.Yield();
        throw new InvalidOperationException("no network");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static iConsole.Test.Core.ScreenBuffer RenderScreen(NetworkBrowserDialog dialog)
    {
        var buffer = new iConsole.Test.Core.ScreenBuffer(120, 40);
        void Draw(iConsole.Controls.IControl control)
        {
            if (!control.IsVisible)
            {
                return;
            }

            control.Draw(buffer);
            foreach (var child in control.Controls.AsSnapshot())
            {
                Draw(child);
            }
        }

        Draw(dialog);
        return buffer;
    }


    [TestMethod]
    public async Task DeletingASavedConnectionAsksFirst()
    {
        // 'd' removed the profile and its stored password on the spot.
        var profile = new NetworkConnectionProfile { Protocol = NetworkProtocol.Ssh, Host = "host1", Username = "alice" };
        var (dialog, _, profiles, vault, _) = CreateDialog(savedProfiles: [profile]);
        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);

        await dialog.HandleKeyEventAsync(new KeyEvent('d', ConsoleKey.D, false, false, false));

        Assert.IsTrue(dialog.IsConfirmingDeleteForTest);
        profiles.Verify(s => s.Delete(It.IsAny<string>()), Times.Never);

        await dialog.HandleKeyEventAsync(new KeyEvent('q', ConsoleKey.Q, false, false, false));
        Assert.IsTrue(dialog.IsConfirmingDeleteForTest, "a stray key is not an answer");

        await dialog.HandleKeyEventAsync(new KeyEvent('y', ConsoleKey.Y, false, false, false));

        profiles.Verify(s => s.Delete(profile.Id), Times.Once);
        vault.Verify(s => s.DeleteSecret(profile.Id), Times.Once);
        Assert.IsFalse(dialog.HostRowsForTest.Any(r => r.Kind == NetworkBrowserDialog.HostRowKind.SavedProfile));
    }

    [TestMethod]
    public async Task NoKeepsTheSavedConnection()
    {
        var profile = new NetworkConnectionProfile { Protocol = NetworkProtocol.Ssh, Host = "host1", Username = "alice" };
        var (dialog, _, profiles, _, _) = CreateDialog(savedProfiles: [profile]);
        var root = await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);

        await dialog.HandleKeyEventAsync(new KeyEvent('d', ConsoleKey.D, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent('\x1b', ConsoleKey.Escape, false, false, false));

        Assert.IsFalse(dialog.IsConfirmingDeleteForTest);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "Esc answers the question, it does not close the dialog");
        profiles.Verify(s => s.Delete(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task AScanErrorStaysOnScreen()
    {
        // The finally block cleared the status a moment after the error was set.
        var (dialog, discovery, _, _, _) = CreateDialog();
        discovery.Setup(s => s.ScanAsync(It.IsAny<CancellationToken>())).Returns(FailingScan());

        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, _ => dialog.StatusForTest.Contains("no network"), timeoutMs: 20000);
        await Task.Delay(100);

        StringAssert.Contains(dialog.StatusForTest, "Scan error: no network");
        Assert.IsTrue(RenderScreen(dialog).Contains("r:rescan"), "the keys stay beside the error");
    }

    [TestMethod]
    public async Task AnEmptyNetworkSaysSoAndHowToRetry()
    {
        var (dialog, _, _, _, _) = CreateDialog();
        await ShowAsync(dialog);

        await WaitForRowsAsync(dialog, _ => RenderScreen(dialog).Contains("No hosts found"), timeoutMs: 20000);
    }

    [TestMethod]
    public async Task TheConnectFormNamesTheHostAndProtocol()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.5"), SshAvailable: true, SmbAvailable: false, Shares: []);
        var (dialog, _, _, _, _) = CreateDialog(scanResults: [host]);
        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);

        await dialog.SelectAsyncForTest();

        Assert.AreEqual("Connect to 10.0.0.5 over SSH", dialog.SectionTitle());
        Assert.IsTrue(RenderScreen(dialog).Contains("Connect to 10.0.0.5 over SSH"));
    }

    [TestMethod]
    public async Task EnterInTheConnectFormConnects()
    {
        // The footer said Enter connects; from the text fields it did nothing.
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.5"), SshAvailable: true, SmbAvailable: false, Shares: []);
        var (dialog, _, _, _, contentModels) = CreateDialog(scanResults: [host]);
        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);
        await dialog.SelectAsyncForTest();
        dialog.UsernameTextBoxForTest.Text = "bob";

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        contentModels.Verify(s => s.SetUrlAsync(It.Is<Url>(u => u.FullUrl.Contains("bob@10.0.0.5"))), Times.Once);
    }

    [TestMethod]
    public async Task AFailedConnectionIsShownAndTheFormStays()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.5"), SshAvailable: true, SmbAvailable: false, Shares: []);
        var (dialog, _, _, _, contentModels) = CreateDialog(scanResults: [host]);
        contentModels.Setup(s => s.SetUrlAsync(It.IsAny<Url>())).ThrowsAsync(new InvalidOperationException("auth failed"));
        var root = await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);
        await dialog.SelectAsyncForTest();

        await dialog.ConnectPendingAsyncForTest();

        Assert.AreEqual("Connection failed: auth failed", dialog.StatusForTest);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
        Assert.AreEqual(NetworkBrowserDialog.Mode.Connect, dialog.ModeForTest);
    }

    [TestMethod]
    public async Task AnSmbHostNeedsAShareName()
    {
        var host = new DiscoveredHost(IPAddress.Parse("10.0.0.6"), SshAvailable: false, SmbAvailable: true, Shares: []);
        var (dialog, _, _, _, contentModels) = CreateDialog(scanResults: [host]);
        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog, rows => rows.Count > 0);
        dialog.SelectRowForTest(0);
        await dialog.SelectAsyncForTest();
        Assert.AreEqual("Connect to 10.0.0.6 over SMB", dialog.SectionTitle());

        await dialog.ConnectPendingAsyncForTest();

        Assert.AreEqual("A share name is required.", dialog.StatusForTest);
        contentModels.Verify(s => s.SetUrlAsync(It.IsAny<Url>()), Times.Never);
    }

    [TestMethod]
    public async Task RTriggersANewScan()
    {
        var (dialog, discovery, _, _, _) = CreateDialog();
        await ShowAsync(dialog);
        int Scans() => discovery.Invocations.Count(i => i.Method.Name == "ScanAsync");

        // Let the opening scan run first: an 'r' that lands while it is still queued supersedes it
        // before it starts, and then only one scan ever runs — which is right, but not this test.
        await WaitForRowsAsync(dialog, _ => Scans() >= 1 && dialog.StatusForTest.Length == 0, timeoutMs: 20000);

        await dialog.HandleKeyEventAsync(new KeyEvent('r', ConsoleKey.R, false, false, false));

        await WaitForRowsAsync(dialog, _ => Scans() >= 2, timeoutMs: 20000);
    }
}
