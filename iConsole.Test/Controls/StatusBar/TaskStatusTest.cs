using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using Moq;


namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class TaskStatusTest
{
    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var mockThemeService = new Mock<IThemeService>();
        var taskManager = new TaskManager();

        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object);

        Assert.IsNotNull(taskStatus);
    }

    [TestMethod]
    public void Constructor_SetsDefaultViewSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var taskManager = new TaskManager();

        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object);

        Assert.AreEqual(10, taskStatus.ViewSize.Width);
        Assert.AreEqual(1, taskStatus.ViewSize.Height);
    }

    [TestMethod]
    public void OnIdle_UpdatesTextWithTaskCount()
    {
        var mockThemeService = new Mock<IThemeService>();
        var taskManager = new TaskManager();
        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object)
        {
            ViewSize = new Size(20, 1)
        };

        taskStatus.OnIdle();

        Assert.IsTrue(taskStatus.Text.Contains("Tasks:"));
    }

    [TestMethod]
    public void OnIdle_ShowsZeroTasksByDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        var taskManager = new TaskManager();
        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object)
        {
            ViewSize = new Size(20, 1)
        };

        taskStatus.OnIdle();

        Assert.IsTrue(taskStatus.Text.Contains("0"));
    }

    [TestMethod]
    public void OnIdle_PadsTextToViewWidth()
    {
        var mockThemeService = new Mock<IThemeService>();
        var taskManager = new TaskManager();
        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object)
        {
            ViewSize = new Size(20, 1)
        };

        taskStatus.OnIdle();

        Assert.AreEqual(20, taskStatus.Text.Length);
    }

    [TestMethod]
    public void TaskStatus_IsInstanceOfStatusBarItem()
    {
        var mockThemeService = new Mock<IThemeService>();
        var taskManager = new TaskManager();

        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object);

        Assert.IsInstanceOfType(taskStatus, typeof(StatusBarItem));
    }

    [TestMethod]
    public void OnIdle_DoesNotThrow()
    {
        var mockThemeService = new Mock<IThemeService>();
        var taskManager = new TaskManager();
        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object)
        {
            ViewSize = new Size(20, 1)
        };

        // Should not throw
        taskStatus.OnIdle();
    }

    [TestMethod]
    public void Draw_RendersTextToBuffer()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var taskManager = new TaskManager();
        var buffer = new MockBuffer(new Size(20, 1));
        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };
        taskStatus.OnIdle();

        taskStatus.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Tasks:"));
    }

    [TestMethod]
    public void Draw_BeforeOnIdle_DrawsEmptyText()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var taskManager = new TaskManager();
        var buffer = new MockBuffer(new Size(20, 1));
        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        taskStatus.Draw(buffer);

        // Text is empty before OnIdle, but padded to ViewSize width
        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual(20, lines[0].Length);
    }

    [TestMethod]
    public void Draw_TextPaddedToViewWidth()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var taskManager = new TaskManager();
        var buffer = new MockBuffer(new Size(20, 1));
        var taskStatus = new global::iConsole.Controls.StatusBar.TaskStatus(taskManager, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };
        taskStatus.OnIdle();

        taskStatus.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual(20, lines[0].Length);
    }
}
