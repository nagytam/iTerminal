using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs.Remote;
using iTerminal.Core.Model.Remote;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Remote;
using iTerminal.Core.Services.Shell;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Remote;

// Some tests put text on the ambient clipboard and paste it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class RemoteConnectionManagerDialogTest
{
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
        return new Harness(new RemoteConnectionManagerDialog(connections, shell.Object, panels.Object, TestTheme()), connections, shell, panels);
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

    private static Task EnterAsync(RemoteConnectionManagerDialog dialog) =>
        dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

    [TestMethod]
    public async Task AnEmptyManagerSaysHowToAddAConnection()
    {
        var h = Create();

        await ShowAsync(h.Dialog);

        AssertScreenContains(Render(h.Dialog), "N adds an SSH or PowerShell connection");
    }

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

    [TestMethod]
    public async Task SpaceOnTheTypeFieldSwitchesToPowerShell()
    {
        var h = Create();
        await ShowAsync(h.Dialog);

        await h.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(h.Dialog, "win");
        await TabAsync(h.Dialog);
        await h.Dialog.HandleKeyEventAsync(Char(' '));
        await TabAsync(h.Dialog);
        await TypeAsync(h.Dialog, "srv01");
        await EnterAsync(h.Dialog);

        Assert.AreEqual(RemoteProtocol.PowerShell, h.Connections.GetAll().Single().Protocol);
    }

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

    [TestMethod]
    public async Task TheMouseLeavesTheListAloneInTheEditForm()
    {
        var h = await OpenWithAsync(5);
        await h.Dialog.HandleKeyEventAsync(Char('n'));

        await h.Dialog.HandleMouseEventAsync(At(h.Dialog, 3));

        Assert.AreEqual("Edit", h.Dialog.ModeForTest);
        Assert.AreEqual(0, h.Dialog.CursorForTest);
    }
}
