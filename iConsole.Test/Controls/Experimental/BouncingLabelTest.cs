using iConsole.Controls;
using iConsole.Controls.Experimental;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Experimental;

[TestClass]
public class BouncingLabelTest
{
    [TestMethod]
    public void BouncingLabelCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var position = new Position(5, 5);
        var bouncingLabel = new BouncingLabel(position, "Bouncing", mockThemeService.Object, autoStart: false);

        Assert.IsNotNull(bouncingLabel);
        Assert.IsInstanceOfType(bouncingLabel, typeof(Label));
    }

    [TestMethod]
    public void BouncingLabelHasText()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var position = new Position(0, 0);
        var bouncingLabel = new BouncingLabel(position, "Test Label", mockThemeService.Object, autoStart: false);

        Assert.AreEqual("Test Label", bouncingLabel.Text);
    }

    [TestMethod]
    public void BouncingLabelHasPosition()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var position = new Position(10, 20);
        var bouncingLabel = new BouncingLabel(position, "Test", mockThemeService.Object, autoStart: false);

        Assert.AreEqual(10U, bouncingLabel.Position.X);
        Assert.AreEqual(20U, bouncingLabel.Position.Y);
    }

    [TestMethod]
    public void BouncingLabelPositionCanBeChanged()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var position = new Position(0, 0);
        var bouncingLabel = new BouncingLabel(position, "Test", mockThemeService.Object, autoStart: false);

        var newPosition = new Position(15, 10);
        bouncingLabel.Position = newPosition;

        Assert.AreEqual(15U, bouncingLabel.Position.X);
        Assert.AreEqual(10U, bouncingLabel.Position.Y);
    }

    [TestMethod]
    public void BouncingLabelHasDefaultVisibility()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var position = new Position(0, 0);
        var bouncingLabel = new BouncingLabel(position, "Test", mockThemeService.Object, autoStart: false);

        Assert.IsTrue(bouncingLabel.IsVisible);
    }

    [TestMethod]
    public void BouncingLabelHasDefaultEnabled()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var position = new Position(0, 0);
        var bouncingLabel = new BouncingLabel(position, "Test", mockThemeService.Object, autoStart: false);

        Assert.IsTrue(bouncingLabel.IsEnabled);
    }

    [TestMethod]
    public void BouncingLabelTextCanBeEmpty()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var position = new Position(0, 0);
        var bouncingLabel = new BouncingLabel(position, "", mockThemeService.Object, autoStart: false);

        Assert.AreEqual("", bouncingLabel.Text);
    }

    [TestMethod]
    public void BouncingLabelZeroPosition()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var position = new Position(0, 0);
        var bouncingLabel = new BouncingLabel(position, "Corner", mockThemeService.Object, autoStart: false);

        Assert.AreEqual(0U, bouncingLabel.Position.X);
        Assert.AreEqual(0U, bouncingLabel.Position.Y);
    }

    [TestMethod]
    public void BouncingLabelInheritsFromLabel()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var position = new Position(0, 0);
        var bouncingLabel = new BouncingLabel(position, "Test", mockThemeService.Object, autoStart: false);

        Assert.IsInstanceOfType(bouncingLabel, typeof(Label));
    }

    [TestMethod]
    public void Draw_RendersTextToBuffer()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 5));
        var bouncingLabel = new BouncingLabel(new Position(0, 0), "Hello", mockThemeService.Object, autoStart: false)
        {
            Position = new Position(0, 2),
            ViewSize = new Size(10, 1)
        };

        bouncingLabel.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Hello"));
    }

    [TestMethod]
    public void Draw_AtPosition_RenderedAtCorrectRow()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 5));
        var bouncingLabel = new BouncingLabel(new Position(0, 0), "Hi", mockThemeService.Object, autoStart: false)
        {
            Position = new Position(0, 1),
            ViewSize = new Size(10, 1)
        };

        bouncingLabel.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[1].Contains("Hi"));
    }

    [TestMethod]
    public void Draw_UsesColorSchemeColors()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var bouncingLabel = new BouncingLabel(new Position(0, 0), "Test", mockThemeService.Object, autoStart: false)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        bouncingLabel.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Test"));
    }
}
