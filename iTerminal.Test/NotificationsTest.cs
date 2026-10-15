using iConsole.Services;

namespace iTerminal.Test;

/// <summary>
/// Notifications, end to end: an operation that finishes says so in a toast, the toast is drawn over
/// whatever is on screen without taking the keyboard, and it leaves by itself — through the real
/// <see cref="iConsole.Jobs.NotificationJob"/>, the real renderer and its layers.
/// </summary>
[TestClass]
public class NotificationsTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private INotificationService Notifications => _app.Driver.GetRequiredService<INotificationService>();

    [TestMethod]
    public async Task AFinishedCopyIsAnnounced_AndTheToastLeavesByItself()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy");
        await _app.PressAsync("Enter");

        var screen = await _app.WaitForTextAsync("✓ Copy file finished");

        // In the right half, under the menu bar — and the panels' own rows are still there.
        var position = screen.Find("✓ Copy file finished")!;
        Assert.IsTrue(position.X > _app.PanelWidth, screen.Dump());
        Assert.AreEqual(1, position.Y, screen.Dump());
        Assert.IsTrue(_app.LeftPanelLists("readme.txt", screen), screen.Dump());

        // The toast has no say over the keyboard: the panel under it still moves its cursor.
        await _app.WaitForNoDialogAsync();
        await _app.SelectEntryAsync("alpha");
        Assert.IsTrue(_app.Screen.Contains("Copy file finished"), "the toast is still up while the cursor moves");

        // Four seconds on screen, then gone — with nothing of it left behind.
        screen = await _app.WaitForTextGoneAsync("Copy file finished", TimeSpan.FromSeconds(15));
        Assert.AreEqual(0, Notifications.Active.Count);
        Assert.IsFalse(screen.Contains("▌"), screen.Dump());
        await _app.WaitUntilAsync(s => _app.RightPanelLists("readme.txt", s), "the right panel to be whole again");
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task AToastStaysOnTopOfADialog_AndAClickDismissesIt()
    {
        Notifications.Notify("Index rebuilt", "1,234 documents", NotificationSeverity.Info, Timeout.InfiniteTimeSpan);
        await _app.WaitForTextAsync("Index rebuilt");

        // A dialog opened afterwards is drawn on a higher layer than the panels: the toast moves above it.
        await _app.PressAsync("Ctrl+G");
        await _app.Driver.WaitForModalAsync<Core.Dialogs.Git.GitClientDialog>();
        await _app.Driver.WaitForIdleAsync();
        var screen = await _app.WaitForTextAsync("Index rebuilt");
        Assert.IsTrue(screen.Contains("1,234 documents"), screen.Dump());

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextAsync("Index rebuilt");

        await _app.Driver.ClickAsync("Index rebuilt");

        await _app.WaitForTextGoneAsync("Index rebuilt");
        Assert.AreEqual(0, Notifications.Active.Count);
        await _app.WaitForNoStrayFramesAsync();
    }
}
