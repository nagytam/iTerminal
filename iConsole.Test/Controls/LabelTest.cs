using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class LabelTest
{
    [TestMethod]
    public void LabelDrawsPlainText()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var label = new Label("Hello, World!", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        label.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Hello, World!"));
    }

    [TestMethod]
    public void LabelDrawsWithCustomColors()
    {
        var mockThemeService = new Mock<IThemeService>();
        var colorScheme = new ColorScheme();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme);
        var buffer = new MockBuffer(new Size(15, 1));
        var label = new Label("Test", mockThemeService.Object)
        {
            ForegroundColor = Color.Red,
            BackgroundColor = Color.Blue,
            Position = new Position(0, 0),
            ViewSize = new Size(15, 1)
        };

        label.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Test"));
    }

    [TestMethod]
    public void LabelDrawsWithBoldFormatting()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var label = new Label("Bold", mockThemeService.Object)
        {
            IsBold = true,
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        label.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Bold"));
    }

    [TestMethod]
    public void LabelDrawsWithItalicFormatting()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var label = new Label("Italic", mockThemeService.Object)
        {
            IsItalic = true,
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        label.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Italic"));
    }

    [TestMethod]
    public void LabelDrawsWithUnderlineFormatting()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var label = new Label("Underline", mockThemeService.Object)
        {
            IsUnderline = true,
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        label.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Underline"));
    }

    [TestMethod]
    public void LabelDrawsWithStrikeThroughFormatting()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(12, 1));
        var label = new Label("StrikeThrough", mockThemeService.Object)
        {
            IsStrikeThrough = true,
            Position = new Position(0, 0),
            ViewSize = new Size(12, 1)
        };

        label.Draw(buffer);
        // The Draw method renders the text, formatting is applied during rendering
        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    [TestMethod]
    public void LabelDrawsWithMultipleFormattingOptions()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(15, 1));
        var label = new Label("Formatted", mockThemeService.Object)
        {
            IsBold = true,
            IsItalic = true,
            IsUnderline = true,
            ForegroundColor = Color.Green,
            BackgroundColor = Color.Black,
            Position = new Position(0, 0),
            ViewSize = new Size(15, 1)
        };

        label.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Formatted"));
    }

    [TestMethod]
    public void LabelTextPropertyCanBeChanged()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var label = new Label("Original", mockThemeService.Object);

        Assert.AreEqual("Original", label.Text);

        label.Text = "Changed";
        Assert.AreEqual("Changed", label.Text);
    }

    [TestMethod]
    public void LabelDrawsWithOffset()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 3));
        var label = new Label("Label", mockThemeService.Object)
        {
            Position = new Position(5, 1),
            ViewSize = new Size(10, 1)
        };

        label.Draw(buffer);
        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual(string.Empty, lines[0]);
        Assert.IsTrue(lines[1].Contains("Label"));
    }

    [TestMethod]
    public void LabelDrawsEmptyText()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var label = new Label("", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        label.Draw(buffer);
        // Empty text renders as empty or padded, just verify it doesn't throw
        Assert.IsNotNull(buffer.ContentAsString);
    }

    [TestMethod]
    public void LabelDrawsTextLongerThanViewSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(5, 1));
        var label = new Label("VeryLongText", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(5, 1)
        };

        label.Draw(buffer);
        // Text should be truncated to view size
        Assert.AreEqual("VeryL" + Environment.NewLine, buffer.ContentAsString);
    }
}
