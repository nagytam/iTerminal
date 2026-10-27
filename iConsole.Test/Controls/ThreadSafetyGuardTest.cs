using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>The controls documented as safe to feed from any thread are fed from several while the UI thread draws and presses keys.</summary>
[TestClass]
public class ThreadSafetyGuardTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static async Task Hammer(Control control, Action<int> feed)
    {
        control.Position = new Position(0, 0);
        control.ViewSize = new Size(40, 6);
        using CancellationTokenSource stop = new();
        Task[] writers = [.. Enumerable.Range(0, 4).Select(thread => Task.Run(() =>
        {
            for (int i = 0; i < 400 && !stop.IsCancellationRequested; i++)
            {
                feed(thread * 1000 + i);
            }
        }))];
        Task reader = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                control.OnIdle();
                ScreenBuffer.Render(control, 40, 6);
                await control.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
                await control.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false));
            }
        });

        await Task.WhenAll(writers);
        await stop.CancelAsync();
        await reader;
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task LogView_CanBeFedFromSeveralThreads_WhileItIsDrawn()
    {
        LogView log = new(Theme()) { MaxLines = 200 };
        log.SetFocus();

        await Hammer(log, n => log.Append($"line {n}\nsecond {n}", (LogEntryLevel)(n % 4)));

        Assert.IsTrue(log.Count <= 200);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task LiveTable_CanBeFedFromSeveralThreads_WhileItIsDrawn()
    {
        LiveTable table = new(Theme(), new SystemTimeProvider());
        table.AddColumn("Name", 10);
        table.AddColumn("Value", 10);
        table.SetFocus();

        await Hammer(table, n =>
        {
            table.SetRow("k" + (n % 25), "name " + n, n.ToString());
            table.SetCell("k" + (n % 25), 1, "v" + n);
        });

        Assert.IsNotNull(table);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ProgressGroup_CanBeAdvancedFromSeveralThreads_WhileItIsDrawn()
    {
        iConsole.Controls.Progress.ProgressGroup group = new(Theme(), new SystemTimeProvider());
        iConsole.Controls.Progress.ProgressTask[] tasks = [.. Enumerable.Range(0, 5).Select(i => group.AddTask("task " + i, 1000))];
        group.SetFocus();

        await Hammer(group, n => tasks[n % tasks.Length].Increment(1));

        Assert.IsTrue(tasks.Sum(t => t.Value) > 0);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task TheToastHost_SurvivesNotificationsFromSeveralThreads_WhileItIsDrawn()
    {
        NotificationService service = new();
        ToastHost host = new(service, Theme()) { Position = new Position(0, 0), ViewSize = new Size(60, 12) };

        await Hammer(host, n =>
        {
            service.Notify("title " + n, n % 3 == 0 ? null : "message " + n, (NotificationSeverity)(n % 4), TimeSpan.FromMilliseconds(30));
            if (n % 17 == 0)
            {
                service.DismissAll();
            }
        });

        Assert.IsTrue(service.Version > 0);
    }
}
