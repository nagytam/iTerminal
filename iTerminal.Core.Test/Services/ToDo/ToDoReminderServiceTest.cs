using iConsole.Services;
using iTerminal.Core.Model.ToDo;
using iTerminal.Core.Services;
using iTerminal.Core.Services.ToDo;
using Moq;

namespace iTerminal.Core.Test.Services.ToDo;

[TestClass]
public class ToDoReminderServiceTest
{
    private static readonly DateTime Now = new(2026, 11, 14, 10, 0, 0);

    [Timeout(60_000)]
    [TestMethod]
    public void EachDueReminderIsPostedOnceAsAPersistentWarning()
    {
        var store = new ToDoStore(new Mock<ISettingsService>().Object);
        store.Add(new ToDoTask(Guid.NewGuid(), "Call the bank", null, ToDoPriority.Normal, false, Now.AddMinutes(-5)));
        store.Add(new ToDoTask(Guid.NewGuid(), "Pay rent", null, ToDoPriority.High, false, Now.AddHours(2)));
        var notifications = new Mock<INotificationService>();
        var service = new ToDoReminderService(store, notifications.Object);

        Assert.AreEqual(1, service.GiveDueReminders(Now));
        Assert.AreEqual(0, service.GiveDueReminders(Now));

        notifications.Verify(n => n.Notify(
            It.IsAny<string>(),
            "Call the bank",
            NotificationSeverity.Warning,
            Timeout.InfiniteTimeSpan), Times.Once);
        notifications.Verify(n => n.Notify(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()), Times.Once);
    }
}
