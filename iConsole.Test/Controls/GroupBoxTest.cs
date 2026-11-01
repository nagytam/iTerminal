using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class GroupBoxTest
{
    private static Mock<IThemeService> ThemeMock()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return themeService;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContentSizeIsInteriorOfTheBorder()
    {
        var groupBox = new GroupBox(ThemeMock().Object)
        {
            ViewSize = new Size(20, 6)
        };

        Assert.AreEqual(18, (int)groupBox.ContentSize.Width);
        Assert.AreEqual(4, (int)groupBox.ContentSize.Height);
        Assert.AreEqual(1, (int)groupBox.ContentPosition.X);
        Assert.AreEqual(1, (int)groupBox.ContentPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetContentFillsTheInterior()
    {
        var groupBox = new GroupBox(ThemeMock().Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 6)
        };
        var content = new FormPanel(ThemeMock().Object);

        groupBox.SetContent(content);

        Assert.AreEqual(1, (int)content.Position.X);
        Assert.AreEqual(1, (int)content.Position.Y);
        Assert.AreEqual(18, (int)content.ViewSize.Width);
        Assert.AreEqual(4, (int)content.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DrawRendersTitleInBorder()
    {
        var buffer = new MockBuffer(new Size(20, 6));
        var groupBox = new GroupBox(ThemeMock().Object)
        {
            Title = "Options",
            Position = new Position(0, 0),
            ViewSize = new Size(20, 6)
        };

        groupBox.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Options"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DrawRendersHostedContentInsideTheBorder()
    {
        var buffer = new MockBuffer(new Size(20, 6));
        var groupBox = new GroupBox(ThemeMock().Object)
        {
            Title = "Box",
            Position = new Position(0, 0),
            ViewSize = new Size(20, 6)
        };
        groupBox.SetContent(new Label("Hello", ThemeMock().Object));

        groupBox.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Hello"));
    }
}
