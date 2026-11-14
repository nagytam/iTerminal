using iConsole.Data;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.NetworkTools;
using iTerminal.Core.Services.NetworkTools;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.NetworkTools;

[TestClass]
public class NetworkToolsDialogTest
{
    private static NetworkToolsDialog Create(INetworkToolService service) =>
        new(service, Mock.Of<iConsole.Services.IBeepService>(), new iConsole.Services.DateTimeProviderService(), TestTheme());

    private static Mock<INetworkToolService> ServiceWriting(params string[] lines)
    {
        var service = new Mock<INetworkToolService>();
        service.Setup(s => s.RunAsync(It.IsAny<NetworkToolKind>(), It.IsAny<string>(), It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
            .Returns<NetworkToolKind, string, Action<string>, CancellationToken>((_, _, writeLine, _) =>
            {
                foreach (string line in lines)
                {
                    writeLine(line);
                }

                return Task.CompletedTask;
            });
        return service;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EnterWithNoTargetSaysWhatIsMissingAndRunsNothing()
    {
        var service = ServiceWriting();
        NetworkToolsDialog dialog = Create(service.Object);

        dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter)).GetAwaiter().GetResult();

        Assert.IsFalse(dialog.IsRunning);
        Assert.AreEqual("Type a host name or an address first.", dialog.Status);
        service.Verify(s => s.RunAsync(It.IsAny<NetworkToolKind>(), It.IsAny<string>(), It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheChosenToolRunsOnTheTargetAndItsLinesReachTheOutputOnTheIdlePass()
    {
        var service = ServiceWriting("Reply from 192.0.2.1: time=3 ms", "Sent 1, received 1, lost 0 (0%).");
        NetworkToolsDialog dialog = Create(service.Object);
        Assert.AreEqual(NetworkToolKind.Ping, dialog.ToolBox.SelectedItem!.Kind, "Ping is the first tool, so it is the one chosen by default");
        dialog.TargetBox.Text = " example.com ";

        dialog.Run();
        await dialog.RunTask;
        dialog.ApplyPendingForTest();

        service.Verify(s => s.RunAsync(NetworkToolKind.Ping, "example.com", It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        CollectionAssert.AreEqual(new[] { "Reply from 192.0.2.1: time=3 ms", "Sent 1, received 1, lost 0 (0%)." }, dialog.Output.ToArray());
        Assert.AreEqual("Finished.", dialog.Status);
        Assert.IsFalse(dialog.IsRunning);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATargetTheToolRefusesShowsItsReasonInsteadOfRunningForever()
    {
        var service = new Mock<INetworkToolService>();
        service.Setup(s => s.RunAsync(It.IsAny<NetworkToolKind>(), It.IsAny<string>(), It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Give a port number from 1 to 65535."));
        NetworkToolsDialog dialog = Create(service.Object);
        dialog.TargetBox.Text = "example.com";

        dialog.Run();
        await dialog.RunTask;
        dialog.ApplyPendingForTest();

        Assert.AreEqual("Give a port number from 1 to 65535.", dialog.Status);
        Assert.IsFalse(dialog.IsRunning);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EscapeStopsARunThatIsGoingAndCancelsItsToken()
    {
        CancellationToken seen = default;
        var started = new TaskCompletionSource();
        var service = new Mock<INetworkToolService>();
        service.Setup(s => s.RunAsync(It.IsAny<NetworkToolKind>(), It.IsAny<string>(), It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
            .Returns<NetworkToolKind, string, Action<string>, CancellationToken>(async (_, _, _, token) =>
            {
                seen = token;
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            });
        NetworkToolsDialog dialog = Create(service.Object);
        dialog.TargetBox.Text = "example.com";

        dialog.Run();
        started.Task.GetAwaiter().GetResult();
        dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape)).GetAwaiter().GetResult();

        Assert.IsTrue(seen.IsCancellationRequested);
        Assert.IsTrue(dialog.RunTask.Wait(TimeSpan.FromSeconds(10)));
        dialog.ApplyPendingForTest();
        Assert.AreEqual("Stopped.", dialog.Status);
    }

    [TestMethod]
    public void TheCommandOpensTheDialogUnderItsOwnTitle()
    {
        var command = new NetworkToolsDialogCommand(Create(ServiceWriting().Object));
        Assert.AreEqual("Network tools", command.Title);
    }
}
