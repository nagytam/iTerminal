using iConsole.Data;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Factories;
using iTerminal.Core.Helper;

namespace iTerminal.Core.Test.Helper;

[TestClass]
public class TaskNotificationHelperTest
{
    /// <summary>A task that ends the way the test says, describing itself as the real ones do.</summary>
    private sealed class ScriptedTask(string description, string? error = null, bool cancel = false, string? subject = null) : BackgroundTask
    {
        public override Task ExecuteAsync()
        {
            Subject = subject;
            Description = description;
            Error = error;
            if (cancel)
            {
                CancellationTokenSource.Cancel();
            }
            return Task.CompletedTask;
        }
    }

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    private static async Task<Notification> RunAsync(ScriptedTask task)
    {
        var service = new NotificationService();
        TaskNotificationHelper.NotifyWhenFinished(task, service);
        Assert.AreEqual(0, service.Active.Count, "nothing is said while the task runs");

        await task.RunAsync();

        Assert.AreEqual(1, service.Active.Count);
        return service.Active[0];
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FinishedTransfer_SaysWhatWasCopiedWhere()
    {
        var notification = await RunAsync(new ScriptedTask(Lines("Copy file", @"C:\a\readme.txt", "to", @"D:\b\readme.txt", "finished")));

        Assert.AreEqual("Copy file finished", notification.Title);
        Assert.AreEqual(@"C:\a\readme.txt to D:\b\readme.txt", notification.Message);
        Assert.AreEqual(NotificationSeverity.Success, notification.Severity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASentenceThatSaysEverything_IsTheTitle_AndTheRestTheMessage()
    {
        var notification = await RunAsync(new ScriptedTask(Lines("Compressed 3 file(s) into a.zip", "2 entries kept from the archive")));

        Assert.AreEqual("Compressed 3 file(s) into a.zip", notification.Title);
        Assert.AreEqual("2 entries kept from the archive", notification.Message);
        Assert.AreEqual(NotificationSeverity.Success, notification.Severity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FailedTask_IsAnError_WithTheReasonOnce()
    {
        var notification = await RunAsync(new ScriptedTask("Compress failed: the disk is full", error: "the disk is full"));

        Assert.AreEqual("Compress failed", notification.Title);
        Assert.AreEqual("the disk is full", notification.Message);
        Assert.AreEqual(NotificationSeverity.Error, notification.Severity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PartlyFailedFolderOperation_IsAnError_ThoughTheTaskCompleted()
    {
        var notification = await RunAsync(new ScriptedTask(
            Lines("Delete", @"C:\a\folder", "finished with 2 error(s): x (denied), y (denied)"),
            error: "2 item(s) failed."));

        Assert.AreEqual("Delete failed", notification.Title);
        Assert.AreEqual("2 item(s) failed.", notification.Message);
        Assert.AreEqual(NotificationSeverity.Error, notification.Severity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CancelledTask_IsAWarning()
    {
        var notification = await RunAsync(new ScriptedTask(Lines("Move folder", @"C:\a\folder", "cancelled"), cancel: true));

        Assert.AreEqual("Move folder cancelled", notification.Title);
        Assert.AreEqual(@"C:\a\folder", notification.Message);
        Assert.AreEqual(NotificationSeverity.Warning, notification.Severity);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TransferSubject_IsNamesNotPaths()
    {
        Assert.AreEqual("readme.txt to right",
            TaskNotificationHelper.TransferSubject(new Url("file://C:/left/readme.txt"), new Url("file://C:/right/readme.txt")));
        Assert.AreEqual("alpha to right",
            TaskNotificationHelper.TransferSubject(new Url("file://C:/left/alpha/"), new Url("file://C:/right/alpha/")));
        StringAssert.StartsWith(
            TaskNotificationHelper.TransferSubject(new Url("file://C:/left/a.txt"), new Url("file://C:/right/b.txt")),
            "a.txt to right");
        Assert.AreEqual("a.txt to memdrive:",
            TaskNotificationHelper.TransferSubject(new Url("file://C:/left/a.txt"), new Url("memdrive://a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATaskWithASubject_SaysThat_InsteadOfItsPaths()
    {
        var task = new ScriptedTask(Lines("Copy file", "C:/a/very/long/path/readme.txt", "to", "D:/another/long/path/readme.txt", "finished"), subject: "readme.txt to path");

        var notification = await RunAsync(task);

        Assert.AreEqual("Copy file finished", notification.Title);
        Assert.AreEqual("readme.txt to path", notification.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATaskAlreadyOver_IsNotifiedAtOnce_AndOnlyOnce()
    {
        // A small local copy is finished before the progress window is even built.
        var task = new ScriptedTask("Copy finished: 2 item(s)");
        await task.RunAsync();
        var service = new NotificationService();

        TaskNotificationHelper.NotifyWhenFinished(task, service);
        await task.RunAsync();

        Assert.AreEqual(1, service.History.Count);
        Assert.AreEqual("Copy finished: 2 item(s)", service.History[0].Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAService_DoesNothing()
    {
        TaskNotificationHelper.NotifyWhenFinished(new ScriptedTask("x"), notificationService: null);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ProgressWindowFactory_ArmsTheNotification_ForEveryOperationItShows()
    {
        var service = new NotificationService();
        var factory = new ProgressWindowFactory(new iConsole.Test.Core.TestThemeService(), service);
        var task = new ScriptedTask(Lines("Delete", @"C:\a\old.txt", "finished"));

        factory.Create(task);
        await task.RunAsync();

        Assert.AreEqual("Delete finished", service.Active.Single().Title);
    }
}
