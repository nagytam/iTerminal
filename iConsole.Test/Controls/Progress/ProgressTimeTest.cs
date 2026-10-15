using iConsole.Controls.Progress;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using Moq;


namespace iConsole.Test.Controls.Progress;

[TestClass]
public class ProgressTimeTest
{
    private class TestBackgroundTask : BackgroundTask
    {
        public TestBackgroundTask(string name) : base()
        {
            Name = name;
        }

        public override Task ExecuteAsync()
        {
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public void Constructor_InitializesProperties()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task");

        var progressTime = new ProgressTime(task, mockThemeService.Object);

        Assert.AreSame(task, progressTime.Task);
        Assert.AreEqual(0, progressTime.ViewSize.Width);
        Assert.AreEqual(1, progressTime.ViewSize.Height);
    }

    [TestMethod]
    public void Constructor_InitializesDefaultLabels()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task");

        var progressTime = new ProgressTime(task, mockThemeService.Object);

        Assert.AreEqual("Elapsed time", progressTime.LabelElapsed);
        Assert.AreEqual("Remaining time", progressTime.LabelRemaining);
        Assert.AreEqual("Total time", progressTime.LabelTotal);
    }

    [TestMethod]
    public void LabelElapsed_CanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task");
        var progressTime = new ProgressTime(task, mockThemeService.Object);

        progressTime.LabelElapsed = "Custom Elapsed";

        Assert.AreEqual("Custom Elapsed", progressTime.LabelElapsed);
    }

    [TestMethod]
    public void LabelRemaining_CanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task");
        var progressTime = new ProgressTime(task, mockThemeService.Object);

        progressTime.LabelRemaining = "Custom Remaining";

        Assert.AreEqual("Custom Remaining", progressTime.LabelRemaining);
    }

    [TestMethod]
    public void LabelTotal_CanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task");
        var progressTime = new ProgressTime(task, mockThemeService.Object);

        progressTime.LabelTotal = "Custom Total";

        Assert.AreEqual("Custom Total", progressTime.LabelTotal);
    }

    [TestMethod]
    public void ViewSize_CanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task");
        var progressTime = new ProgressTime(task, mockThemeService.Object);

        progressTime.ViewSize = new Size(100, 2);

        Assert.AreEqual(100, progressTime.ViewSize.Width);
        Assert.AreEqual(2, progressTime.ViewSize.Height);
    }

    [TestMethod]
    public void Draw_TaskStarted_ShowsElapsedLabel()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task"); // defaults to Started
        var buffer = new MockBuffer(new Size(60, 1));
        var progressTime = new ProgressTime(task, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(60, 1)
        };

        progressTime.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Elapsed time"));
    }

    [TestMethod]
    public void Draw_TaskStarted_ShowsRemainingLabel()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task");
        var buffer = new MockBuffer(new Size(60, 1));
        var progressTime = new ProgressTime(task, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(60, 1)
        };

        progressTime.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Remaining time"));
    }

    [TestMethod]
    public void Draw_TaskStarted_ShowsTimeDuration()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task");
        var buffer = new MockBuffer(new Size(60, 1));
        var progressTime = new ProgressTime(task, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(60, 1)
        };

        progressTime.Draw(buffer);

        // Duration shown as hh:mm:ss
        Assert.IsTrue(buffer.ContentAsString.Contains(":"));
    }

    [TestMethod]
    public void Draw_CustomLabelElapsed_ShowsCustomLabel()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var task = new TestBackgroundTask("Test Task"); // defaults to Started
        var buffer = new MockBuffer(new Size(60, 1));
        var progressTime = new ProgressTime(task, mockThemeService.Object)
        {
            LabelElapsed = "Running",
            Position = new Position(0, 0),
            ViewSize = new Size(60, 1)
        };

        progressTime.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Running"));
    }
}

