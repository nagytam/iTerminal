using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class SeparatorTest
{
    private static Mock<IThemeService> ThemeMock()
    {
        var m = new Mock<IThemeService>();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m;
    }

    [TestMethod]
    public void Constructor_DefaultOrientationIsHorizontal()
    {
        var sep = new Separator(ThemeMock().Object);

        Assert.AreEqual(Orientation.Horizontal, sep.Orientation);
    }

    [TestMethod]
    public void Draw_Horizontal_FillsRowWithHorizontalSingleChars()
    {
        var buffer = new MockBuffer(new Size(10, 1));
        var sep = new Separator(ThemeMock().Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        sep.Draw(buffer);

        var line = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.AreEqual(10, line.Length);
        Assert.IsTrue(line.All(c => c == '⎯'));
    }

    [TestMethod]
    public void Draw_Horizontal_RenderedOnCorrectRow()
    {
        var buffer = new MockBuffer(new Size(10, 3));
        var sep = new Separator(ThemeMock().Object)
        {
            Position = new Position(0, 1),
            ViewSize = new Size(10, 1)
        };

        sep.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual(string.Empty, lines[0].Trim());
        Assert.IsTrue(lines[1].All(c => c == '⎯'));
    }

    [TestMethod]
    public void Draw_Vertical_FillsColumnWithVerticalSingleChars()
    {
        var buffer = new MockBuffer(new Size(1, 5));
        var sep = new Separator(ThemeMock().Object)
        {
            Orientation = Orientation.Vertical,
            Position = new Position(0, 0),
            ViewSize = new Size(1, 5)
        };

        sep.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        for (var i = 0; i < 5; i++)
        {
            Assert.AreEqual("│", lines[i], $"Row {i} should be │");
        }
    }

    [TestMethod]
    public void Draw_Vertical_RenderedOnCorrectColumn()
    {
        var buffer = new MockBuffer(new Size(5, 3));
        var sep = new Separator(ThemeMock().Object)
        {
            Orientation = Orientation.Vertical,
            Position = new Position(2, 0),
            ViewSize = new Size(1, 3)
        };

        sep.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        foreach (var line in lines.Take(3))
        {
            Assert.AreEqual('│', line[2], $"Column 2 should be │ in '{line}'");
        }
    }

    [TestMethod]
    public void Draw_Horizontal_SingleCharWidth_RendersSingleChar()
    {
        var buffer = new MockBuffer(new Size(1, 1));
        var sep = new Separator(ThemeMock().Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(1, 1)
        };

        sep.Draw(buffer);

        Assert.AreEqual("⎯", buffer.ContentAsString.Split(Environment.NewLine)[0]);
    }
}
