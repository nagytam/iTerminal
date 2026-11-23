using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs.Remote;
using iTerminal.Core.Model.Remote;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Remote;
using iTerminal.Core.Services.Shell;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Remote;

[TestClass]
public class RemoteConnectionManagerDialogTest
{
    private IDisposable _clipboardScope = null!;

    [TestCleanup]
    public void ReleaseOwnClipboard() => _clipboardScope.Dispose();

    [TestInitialize]
    public void ScopeOwnClipboard() => _clipboardScope = iConsole.Services.TextClipboard.Scope(new iConsole.Services.InProcessTextClipboardService());

    private sealed record Harness(
        RemoteConnectionManagerDialog Dialog,
        RemoteConnectionService Connections,
        Mock<IInteractiveShellService> Shell,
        Mock<IContentModelService> Panels);

    private static Harness Create()
    {
        var connections = new RemoteConnectionService(new SettingsService());
        var shell = new Mock<IInteractiveShellService>();
        shell.Setup(s => s.RunAsync(It.IsAny<ShellLaunch>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ShellOutcome(0, null));
        var panels = new Mock<IContentModelService>();
        panels.Setup(p => p.SetUrlAsync(It.IsAny<Url>())).Returns(Task.CompletedTask);
        return new Harness(new RemoteConnectionManagerDialog(connections, shell.Object, panels.Object, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()), connections, shell, panels);
    }

    private static async Task TypeAsync(RemoteConnectionManagerDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    private static Task TabAsync(RemoteConnectionManagerDialog dialog) =>
        dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));

    private static Task RightAsync(RemoteConnectionManagerDialog dialog) =>
        dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));

    private static Task EnterAsync(RemoteConnectionManagerDialog dialog) =>
        dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyManagerSaysHowToAddAConnection()
    {
        var h = Create();

        await ShowAsync(h.Dialog);

        AssertScreenContains(Render(h.Dialog), "N adds an SSH, PowerShell, FTP or WebDAV connection");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANewSshConnectionIsSavedFromTheForm()
    {
        var h = Create();
        await ShowAsync(h.Dialog);

        await h.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(h.Dialog, "web");
        await TabAsync(h.Dialog); // Type
        await TabAsync(h.Dialog); // Host
        await TypeAsync(h.Dialog, "example.com");
        await TabAsync(h.Dialog); // Port
        await TypeAsync(h.Dialog, "2200");
        await TabAsync(h.Dialog); // User
        await TypeAsync(h.Dialog, "alice");
        await EnterAsync(h.Dialog);

        var saved = h.Connections.GetAll().Single();
        Assert.AreEqual("web", saved.Name);
        Assert.AreEqual(RemoteProtocol.Ssh, saved.Protocol);
        Assert.AreEqual("example.com", saved.Host);
        Assert.AreEqual(2200, saved.Port);
        Assert.AreEqual("alice", saved.Username);
        Assert.AreEqual("List", h.Dialog.ModeForTest);
        AssertScreenContains(Render(h.Dialog), "alice@example.com");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RightOnTheTypeFieldSwitchesToPowerShell()
    {
        var h = Create();
        await ShowAsync(h.Dialog);

        await h.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(h.Dialog, "win");
        await TabAsync(h.Dialog);
        await RightAsync(h.Dialog);
        await TabAsync(h.Dialog);
        await TypeAsync(h.Dialog, "srv01");
        await EnterAsync(h.Dialog);

        Assert.AreEqual(RemoteProtocol.PowerShell, h.Connections.GetAll().Single().Protocol);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABadHostIsNotSavedAndTheFormStaysOpen()
    {
        var h = Create();
        await ShowAsync(h.Dialog);

        await h.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(h.Dialog, "evil");
        await TabAsync(h.Dialog);
        await TabAsync(h.Dialog);
        await TypeAsync(h.Dialog, "-oProxyCommand=x");
        await EnterAsync(h.Dialog);

        Assert.AreEqual(0, h.Connections.GetAll().Count);
        Assert.AreEqual("Edit", h.Dialog.ModeForTest);
        StringAssert.Contains(h.Dialog.StatusForTest, "host");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterLendsTheTerminalToAnSshSession()
    {
        var h = Create();
        h.Connections.Save(new RemoteConnection { Name = "web", Host = "example.com", Username = "alice", Port = 2200 });
        await ShowAsync(h.Dialog);

        await EnterAsync(h.Dialog);
        await h.Dialog.PendingOperation;

        h.Shell.Verify(s => s.RunAsync(
            It.Is<ShellLaunch>(l => l.FileName == "ssh" && l.Arguments.SequenceEqual(new[] { "-p", "2200", "alice@example.com" })),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsNotNull(h.Connections.GetAll().Single().LastConnectedUtc);
        StringAssert.Contains(h.Dialog.StatusForTest, "ended");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASessionThatThrows_SaysWhy_NotLeftConnecting()
    {
        var h = Create();
        h.Shell.Setup(s => s.RunAsync(It.IsAny<ShellLaunch>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no ssh client"));
        h.Connections.Save(new RemoteConnection { Name = "web", Host = "example.com" });
        await ShowAsync(h.Dialog);

        await EnterAsync(h.Dialog);
        await h.Dialog.PendingOperation;

        StringAssert.Contains(h.Dialog.StatusForTest, "no ssh client");
        StringAssert.DoesNotMatch(h.Dialog.StatusForTest, new System.Text.RegularExpressions.Regex("Connecting"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASessionThatCannotStartSaysWhy()
    {
        var h = Create();
        h.Shell.Setup(s => s.RunAsync(It.IsAny<ShellLaunch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellOutcome(-1, "Cannot start ssh: not found"));
        h.Connections.Save(new RemoteConnection { Name = "web", Host = "example.com" });
        await ShowAsync(h.Dialog);

        await EnterAsync(h.Dialog);
        await h.Dialog.PendingOperation;

        StringAssert.Contains(h.Dialog.StatusForTest, "Cannot start ssh");
        Assert.IsNull(h.Connections.GetAll().Single().LastConnectedUtc);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NoSecondSessionStartsWhileOneRuns()
    {
        var h = Create();
        h.Shell.SetupGet(s => s.IsRunning).Returns(true);
        h.Connections.Save(new RemoteConnection { Name = "web", Host = "example.com" });
        await ShowAsync(h.Dialog);

        await EnterAsync(h.Dialog);

        h.Shell.Verify(s => s.RunAsync(It.IsAny<ShellLaunch>(), It.IsAny<CancellationToken>()), Times.Never);
        StringAssert.Contains(h.Dialog.StatusForTest, "already running");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BBrowsesAnSshHostInThePanel()
    {
        var h = Create();
        Url? opened = null;
        h.Panels.Setup(p => p.SetUrlAsync(It.IsAny<Url>())).Callback<Url>(u => opened = u).Returns(Task.CompletedTask);
        h.Connections.Save(new RemoteConnection { Name = "web", Host = "example.com", Username = "alice" });
        await ShowAsync(h.Dialog);

        await h.Dialog.HandleKeyEventAsync(Char('b'));
        await h.Dialog.PendingOperation;

        Assert.IsNotNull(opened);
        Assert.AreEqual("example.com", opened!.Host);
        Assert.AreEqual("alice", opened.Username);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BRefusesAHostOnAnotherPort()
    {
        var h = Create();
        h.Connections.Save(new RemoteConnection { Name = "web", Host = "example.com", Port = 2200 });
        await ShowAsync(h.Dialog);

        await h.Dialog.HandleKeyEventAsync(Char('b'));

        h.Panels.Verify(p => p.SetUrlAsync(It.IsAny<Url>()), Times.Never);
        StringAssert.Contains(h.Dialog.StatusForTest, "port 22");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteAsksFirstAndYConfirms()
    {
        var h = Create();
        h.Connections.Save(new RemoteConnection { Name = "web", Host = "example.com" });
        await ShowAsync(h.Dialog);

        await h.Dialog.HandleKeyEventAsync(Char('d'));
        Assert.AreEqual(1, h.Connections.GetAll().Count);
        await h.Dialog.HandleKeyEventAsync(Char('y'));

        Assert.AreEqual(0, h.Connections.GetAll().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnyOtherKeyCancelsADelete()
    {
        var h = Create();
        h.Connections.Save(new RemoteConnection { Name = "web", Host = "example.com" });
        await ShowAsync(h.Dialog);

        await h.Dialog.HandleKeyEventAsync(Char('d'));
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await h.Dialog.HandleKeyEventAsync(Char('y'));

        Assert.AreEqual(1, h.Connections.GetAll().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlVPastesIntoTheHost_AndOnlyDigitsIntoThePort()
    {
        var h = Create();
        await ShowAsync(h.Dialog);
        await h.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(h.Dialog, "web");
        await TabAsync(h.Dialog); // Type
        await TabAsync(h.Dialog); // Host
        await iConsole.Services.TextClipboard.Current.SetTextAsync(" example.com" + (char)10);
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v', control: true));
        await TabAsync(h.Dialog); // Port
        await iConsole.Services.TextClipboard.Current.SetTextAsync("port 2200!");
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Insert, shift: true));
        await TabAsync(h.Dialog); // User
        await TypeAsync(h.Dialog, "alice");
        await EnterAsync(h.Dialog);

        var saved = h.Connections.GetAll().Single();
        Assert.AreEqual("example.com", saved.Host);
        Assert.AreEqual(2200, saved.Port);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlVOnTheTypeFieldDoesNothing_AndCtrlUClearsAField()
    {
        var h = Create();
        await ShowAsync(h.Dialog);
        await h.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(h.Dialog, "web");
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.U, 'u', control: true));
        await TypeAsync(h.Dialog, "db");
        await TabAsync(h.Dialog); // Type
        await iConsole.Services.TextClipboard.Current.SetTextAsync("ignored");

        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v', control: true));
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.U, 'u', control: true));
        await TabAsync(h.Dialog); // Host
        await TypeAsync(h.Dialog, "srv");
        await EnterAsync(h.Dialog);

        var saved = h.Connections.GetAll().Single();
        Assert.AreEqual("db", saved.Name);
        Assert.AreEqual("srv", saved.Host);
        Assert.AreEqual(RemoteProtocol.Ssh, saved.Protocol);
    }

    // The list starts after the border, the heading and the column header: three rows down, two columns in.
    private static iConsole.Data.MouseEvent At(iConsole.Controls.Control dialog, int row, int column = 8, int wheel = 0, bool click = true) => new()
    {
        Position = new iConsole.Data.Position(dialog.AbsolutePosition.X + 2 + column, dialog.AbsolutePosition.Y + 3 + row),
        LeftClicked = click && wheel == 0,
        WheelDelta = wheel,
    };

    private static async Task<Harness> OpenWithAsync(int count)
    {
        var h = Create();
        for (int i = 0; i < count; i++)
        {
            h.Connections.Save(new RemoteConnection { Name = "host" + i.ToString("00"), Host = "h" + i + ".example.com", Username = "alice" });
        }

        await ShowAsync(h.Dialog);
        return h;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickHighlightsAConnection_AndTheWheelMovesThreeAtATime()
    {
        var h = await OpenWithAsync(10);

        Assert.IsTrue(await h.Dialog.HandleMouseEventAsync(At(h.Dialog, 4)));
        Assert.AreEqual(4, h.Dialog.CursorForTest);

        await h.Dialog.HandleMouseEventAsync(At(h.Dialog, 0, wheel: -1));
        Assert.AreEqual(7, h.Dialog.CursorForTest);
        await h.Dialog.HandleMouseEventAsync(At(h.Dialog, 0, wheel: 1));
        Assert.AreEqual(4, h.Dialog.CursorForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickBelowTheLastConnectionDoesNothing_AndTheWheelStopsAtTheEnds()
    {
        var h = await OpenWithAsync(3);

        await h.Dialog.HandleMouseEventAsync(At(h.Dialog, 9));
        Assert.AreEqual(0, h.Dialog.CursorForTest);
        await h.Dialog.HandleMouseEventAsync(At(h.Dialog, 0, wheel: -1));
        Assert.AreEqual(2, h.Dialog.CursorForTest);
        await h.Dialog.HandleMouseEventAsync(At(h.Dialog, 0, wheel: 1));
        await h.Dialog.HandleMouseEventAsync(At(h.Dialog, 0, wheel: 1));
        Assert.AreEqual(0, h.Dialog.CursorForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheMouseLeavesTheListAloneInTheEditForm()
    {
        var h = await OpenWithAsync(5);
        await h.Dialog.HandleKeyEventAsync(Char('n'));

        await h.Dialog.HandleMouseEventAsync(At(h.Dialog, 3));

        Assert.AreEqual("Edit", h.Dialog.ModeForTest);
        Assert.AreEqual(0, h.Dialog.CursorForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlWDeletesTheLastWordOfAField_ButNotOnTheTypeToggle()
    {
        var h = Create();
        await ShowAsync(h.Dialog);
        await h.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(h.Dialog, "build server");

        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.W, 'w', control: true));
        await TabAsync(h.Dialog); // Type
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.W, 'w', control: true));
        await TabAsync(h.Dialog); // Host
        await TypeAsync(h.Dialog, "srv");
        await EnterAsync(h.Dialog);

        var saved = h.Connections.GetAll().Single();
        Assert.AreEqual("build", saved.Name);
        Assert.AreEqual(RemoteProtocol.Ssh, saved.Protocol);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HomeAndEndJumpToTheFirstAndLastConnection()
    {
        var h = await OpenWithAsync(10);

        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(9, h.Dialog.CursorForTest);
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, h.Dialog.CursorForTest);
    }

    private sealed record FileDriveHarness(Harness Base, NetworkProfileService Profiles, Mock<IPasswordManager> Passwords);

    private static FileDriveHarness CreateWithLogins()
    {
        var settings = new SettingsService();
        var profiles = new NetworkProfileService(settings);
        var passwords = new Mock<IPasswordManager>();
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        var connections = new RemoteConnectionService(settings, profiles, passwords.Object);
        var shell = new Mock<IInteractiveShellService>();
        var panels = new Mock<IContentModelService>();
        panels.Setup(p => p.SetUrlAsync(It.IsAny<Url>())).Returns(Task.CompletedTask);
        var dialog = new RemoteConnectionManagerDialog(connections, shell.Object, panels.Object, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        return new FileDriveHarness(new Harness(dialog, connections, shell, panels), profiles, passwords);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnFtpConnectionIsSavedFromTheFormWithItsPasswordInThePasswordManager()
    {
        var h = CreateWithLogins();
        await ShowAsync(h.Base.Dialog);

        await h.Base.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(h.Base.Dialog, "files");
        await TabAsync(h.Base.Dialog); // Type
        await RightAsync(h.Base.Dialog); // PowerShell
        await RightAsync(h.Base.Dialog); // FTP
        await TabAsync(h.Base.Dialog); // Host
        await TypeAsync(h.Base.Dialog, "ftp.example.com");
        await TabAsync(h.Base.Dialog); // Port
        await TabAsync(h.Base.Dialog); // User
        await TypeAsync(h.Base.Dialog, "alice");
        await TabAsync(h.Base.Dialog); // Notes: an FTP entry has no key file and no transport, so Tab skips them
        await TabAsync(h.Base.Dialog); // Password
        await TypeAsync(h.Base.Dialog, "s3cret");
        await TabAsync(h.Base.Dialog); // Folder
        await TypeAsync(h.Base.Dialog, "pub");
        await EnterAsync(h.Base.Dialog);

        var saved = h.Base.Connections.GetAll().Single();
        Assert.AreEqual(RemoteProtocol.Ftp, saved.Protocol);
        Assert.AreEqual("pub", saved.RemotePath);
        Assert.IsTrue(saved.HasSavedCredential);
        h.Passwords.Verify(p => p.Save(It.Is<iTerminal.Core.Model.Passwords.PasswordEntry>(e => e.Id == saved.Id && e.Password == "s3cret")), Times.Once);
        var profile = h.Profiles.Get(saved.Id);
        Assert.IsNotNull(profile);
        Assert.AreEqual(iTerminal.Core.Model.Network.NetworkProtocol.Ftp, profile.Protocol);
        Assert.AreEqual("ftp.example.com", profile.Host);
        Assert.AreEqual("alice", profile.Username);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(saved).Contains("s3cret", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAnFtpConnectionOpensItsDriveInThePanel()
    {
        var h = CreateWithLogins();
        Url? opened = null;
        h.Base.Panels.Setup(p => p.SetUrlAsync(It.IsAny<Url>())).Callback<Url>(u => opened = u).Returns(Task.CompletedTask);
        h.Base.Connections.Save(new RemoteConnection
        {
            Name = "files", Protocol = RemoteProtocol.Ftp, Host = "ftp.example.com", Port = 2121, Username = "alice", RemotePath = "pub",
        });
        await ShowAsync(h.Base.Dialog);

        await EnterAsync(h.Base.Dialog);
        await h.Base.Dialog.PendingOperation;

        Assert.IsNotNull(opened);
        Assert.AreEqual("ftp://alice@ftp.example.com:2121/pub", opened!.FullUrl);
        h.Base.Shell.Verify(s => s.RunAsync(It.IsAny<ShellLaunch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RightOnTheTypeFieldCyclesThroughWebDavOverHttpAndHttps()
    {
        var h = CreateWithLogins();
        await ShowAsync(h.Base.Dialog);
        await h.Base.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(h.Base.Dialog, "cloud");
        await TabAsync(h.Base.Dialog); // Type
        for (var i = 0; i < 4; i++) // PowerShell, FTP, WebDAV over HTTP, WebDAV over HTTPS
        {
            await RightAsync(h.Base.Dialog);
        }

        await TabAsync(h.Base.Dialog); // Host
        await TypeAsync(h.Base.Dialog, "dav.example.com");
        await EnterAsync(h.Base.Dialog);

        var saved = h.Base.Connections.GetAll().Single();
        Assert.AreEqual(RemoteProtocol.WebDav, saved.Protocol);
        Assert.IsTrue(saved.Secure);
        Assert.AreEqual("davs://dav.example.com/", saved.DriveUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnAnEightyColumnScreen_TheHeaderSentenceIsNotCutOff()
    {
        var h = Create();
        await ShowAsync(h.Dialog, new Size(80, 24));

        // The header explaining where passwords live was cut in the middle ("keep theirs in") at 80 columns.
        AssertScreenContains(Render(h.Dialog, new Size(80, 24)), "FTP/WebDAV use the password manager");
    }

    private static async Task<Harness> OpenFormAsync()
    {
        var h = Create();
        await ShowAsync(h.Dialog);
        await h.Dialog.HandleKeyEventAsync(Char('n'));
        return h;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATypoInTheMiddleOfAHostIsFixedInPlace()
    {
        var h = await OpenFormAsync();
        await TypeAsync(h.Dialog, "web");
        await TabAsync(h.Dialog); // Type
        await TabAsync(h.Dialog); // Host
        await TypeAsync(h.Dialog, "exmple.com");
        for (var i = 0; i < 8; i++)
        {
            await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        }

        await TypeAsync(h.Dialog, "a");
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Home));
        await TypeAsync(h.Dialog, "my.");
        await EnterAsync(h.Dialog);

        Assert.AreEqual("my.example.com", h.Connections.GetAll().Single().Host);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASelectionInAFieldIsCopiedAndPastedIntoAnother()
    {
        var h = await OpenFormAsync();
        await TypeAsync(h.Dialog, "build-server");
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.A, 'a', control: true));
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.C, 'c', control: true));
        await TabAsync(h.Dialog); // Type
        await TabAsync(h.Dialog); // Host
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v', control: true));
        await EnterAsync(h.Dialog);

        var saved = h.Connections.GetAll().Single();
        Assert.AreEqual("build-server", saved.Name);
        Assert.AreEqual("build-server", saved.Host);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscOnAFormNothingWasTypedInGoesStraightBack()
    {
        var h = await OpenFormAsync();

        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.AreEqual("List", h.Dialog.ModeForTest);
        Assert.AreEqual(string.Empty, h.Dialog.StatusForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscAfterTypingAsksFirst_AndYDiscardsTheEntry()
    {
        var h = await OpenFormAsync();
        await TypeAsync(h.Dialog, "web");

        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.AreEqual("Edit", h.Dialog.ModeForTest);
        StringAssert.Contains(h.Dialog.StatusForTest, "Discard");
        AssertScreenContains(Render(h.Dialog), "Discard the changes?");

        await h.Dialog.HandleKeyEventAsync(Char('y'));

        Assert.AreEqual("List", h.Dialog.ModeForTest);
        Assert.AreEqual(0, h.Connections.GetAll().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnyOtherKeyAnswersTheDiscardQuestionWithNo_AndTheTypedTextIsKept()
    {
        var h = await OpenFormAsync();
        await TypeAsync(h.Dialog, "web");
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        await h.Dialog.HandleKeyEventAsync(Char('x'));

        Assert.AreEqual("Edit", h.Dialog.ModeForTest);
        StringAssert.DoesNotMatch(h.Dialog.StatusForTest, new System.Text.RegularExpressions.Regex("Discard"));
        await TabAsync(h.Dialog); // Type
        await TabAsync(h.Dialog); // Host
        await TypeAsync(h.Dialog, "srv");
        await EnterAsync(h.Dialog);
        Assert.AreEqual("web", h.Connections.GetAll().Single().Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangingOnlyTheTypeCountsAsAChange()
    {
        var h = await OpenFormAsync();
        await TabAsync(h.Dialog); // Type
        await RightAsync(h.Dialog);

        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.AreEqual("Edit", h.Dialog.ModeForTest);
        StringAssert.Contains(h.Dialog.StatusForTest, "Discard");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EditingAnEntryAndLeavingItAsItWasAsksNothing()
    {
        var h = Create();
        h.Connections.Save(new RemoteConnection { Name = "web", Host = "example.com" });
        await ShowAsync(h.Dialog);
        await h.Dialog.HandleKeyEventAsync(Char('e'));

        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.AreEqual("List", h.Dialog.ModeForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFormShowsItsLabels_AndThePasswordIsNotDrawnInTheClear()
    {
        var h = CreateWithLogins();
        await ShowAsync(h.Base.Dialog);
        await h.Base.Dialog.HandleKeyEventAsync(Char('n'));
        await TabAsync(h.Base.Dialog); // Type
        await RightAsync(h.Base.Dialog);
        await RightAsync(h.Base.Dialog); // FTP
        for (var i = 0; i < 5; i++) // Host, Port, User, Notes (Key and Transport are skipped), Password
        {
            await TabAsync(h.Base.Dialog);
        }

        await TypeAsync(h.Base.Dialog, "s3cret");

        var screen = Render(h.Base.Dialog);
        AssertScreenContains(screen, "Password");
        AssertScreenContains(screen, "Folder");
        AssertScreenContains(screen, "\u25cf\u25cf\u25cf\u25cf\u25cf\u25cf");
        AssertScreenDoesNotContain(screen, "s3cret");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APortTakesDigitsOnly_AndShowsTheDefaultWhileEmpty()
    {
        var h = await OpenFormAsync();
        AssertScreenContains(Render(h.Dialog), "(default 22)");
        await TabAsync(h.Dialog); // Type
        await TabAsync(h.Dialog); // Host
        await TypeAsync(h.Dialog, "h.example.com");
        await TabAsync(h.Dialog); // Port

        await TypeAsync(h.Dialog, "22x0");

        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t', shift: true)); // Host
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t', shift: true)); // Type
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t', shift: true)); // Name
        await TypeAsync(h.Dialog, "web");
        await EnterAsync(h.Dialog);

        Assert.AreEqual(220, h.Connections.GetAll().Single().Port);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheEditFormDrawsItsFieldsAndItsFooter_AndNothingElse()
    {
        var h = await OpenFormAsync();

        var screen = Render(h.Dialog);

        foreach (var label in new[] { "Name", "Type", "Host", "Port", "User", "Key file", "Transport", "Notes", "Password", "Folder" })
        {
            AssertScreenContains(screen, label);
        }

        AssertScreenContains(screen, "SSH");
        AssertScreenContains(screen, "WebDAV https");
        AssertScreenContains(screen, "Enter or Ctrl+S saves it");
        AssertScreenDoesNotContain(screen, "Nothing saved yet");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFormIsGoneAgainOnTheList()
    {
        var h = await OpenFormAsync();
        await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        var screen = Render(h.Dialog);

        AssertScreenContains(screen, "Nothing saved yet");
        AssertScreenDoesNotContain(screen, "Key file");
    }
}
