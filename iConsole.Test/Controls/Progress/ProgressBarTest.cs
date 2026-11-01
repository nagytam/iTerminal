using iConsole.Controls.Progress;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Progress;

[TestClass]
public class ProgressBarTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var progressBar = new ProgressBar(mockThemeService.Object);

        Assert.IsNotNull(progressBar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarHasDefaultValues()
    {
        var mockThemeService = new Mock<IThemeService>();
        var progressBar = new ProgressBar(mockThemeService.Object);

        Assert.AreEqual(0, progressBar.Min);
        Assert.AreEqual(100, progressBar.Max);
        Assert.AreEqual(0, progressBar.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarMinCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var progressBar = new ProgressBar(mockThemeService.Object);

        progressBar.Min = 10;
        Assert.AreEqual(10, progressBar.Min);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarMaxCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var progressBar = new ProgressBar(mockThemeService.Object);

        progressBar.Max = 200;
        Assert.AreEqual(200, progressBar.Max);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarCurrentCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var progressBar = new ProgressBar(mockThemeService.Object);

        progressBar.Current = 50;
        Assert.AreEqual(50, progressBar.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarLabelCanBeChanged()
    {
        var mockThemeService = new Mock<IThemeService>();
        var progressBar = new ProgressBar(mockThemeService.Object);

        progressBar.Label = "Loading...";
        Assert.AreEqual("Loading...", progressBar.Label);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarDrawsAt0Percent()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(50, 1));
        var progressBar = new ProgressBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(50, 1),
            Current = 0
        };

        progressBar.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("Progress:"));
        Assert.IsTrue(content.Contains("["));
        Assert.IsTrue(content.Contains("]"));
        Assert.IsTrue(content.Contains("0%"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarDrawsAt50Percent()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(50, 1));
        var progressBar = new ProgressBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(50, 1),
            Current = 50
        };

        progressBar.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("50%"));
        Assert.IsTrue(content.Contains("▬"));
        Assert.IsTrue(content.Contains("‧"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarDrawsAt100Percent()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(50, 1));
        var progressBar = new ProgressBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(50, 1),
            Current = 100
        };

        progressBar.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("100%"));
        Assert.IsTrue(content.Contains("▬"));
        Assert.IsFalse(content.Contains("‧"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarClampsWhenAboveRange()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(50, 1));
        var progressBar = new ProgressBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(50, 1),
            Current = 150
        };

        progressBar.Draw(buffer);

        // Past the maximum draws a full bar: drawing nothing left the previous frame on screen.
        Assert.IsTrue(buffer.ContentAsString.Contains("100%"));
        Assert.IsFalse(buffer.ContentAsString.Contains("‧"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarHandlesCustomRange()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(50, 1));
        var progressBar = new ProgressBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(50, 1),
            Current = 75,
            Min = 50,
            Max = 100
        };

        progressBar.Draw(buffer);

        // 75 in range 50-100 = 50% progress
        Assert.IsTrue(buffer.ContentAsString.Contains("50%"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarDraw_CustomLabel_ShowsLabel()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(50, 1));
        var progressBar = new ProgressBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(50, 1),
            Label = "Loading",
            Current = 25
        };

        progressBar.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Loading:"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarInitialViewSizeIsOnePixelHigh()
    {
        var mockThemeService = new Mock<IThemeService>();
        var progressBar = new ProgressBar(mockThemeService.Object);

        Assert.AreEqual(1, (int)progressBar.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressBarChangesStateWhenCurrentChanges()
    {
        var mockThemeService = new Mock<IThemeService>();
        var progressBar = new ProgressBar(mockThemeService.Object);
        var initialCurrent = progressBar.Current;

        progressBar.Current = 50;

        // Current should have changed
        Assert.AreNotEqual(initialCurrent, progressBar.Current);
    }
}
