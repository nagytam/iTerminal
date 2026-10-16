using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class CheckBoxTest
{
    [TestMethod]
    public async Task CheckBoxHandlesKeyEvents()
    {
        var mockThemeService = new Mock<IThemeService>();
        var checkBox = new CheckBox(mockThemeService.Object)
        {
            Label = "Label",
            IsChecked = false,
            ViewSize = new Size(25, 1)
        };
        await checkBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        Assert.IsTrue(checkBox.IsChecked);
    }

    [TestMethod]
    public async Task ClickTogglesCheckBox()
    {
        var mockThemeService = new Mock<IThemeService>();
        var checkBox = new CheckBox(mockThemeService.Object)
        {
            Label = "Label",
            IsChecked = false,
            Position = new Position(0, 0),
            ViewSize = new Size(25, 1)
        };

        await checkBox.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 0), LeftClicked = true });
        Assert.IsTrue(checkBox.IsChecked);

        await checkBox.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 0), LeftClicked = true });
        Assert.IsFalse(checkBox.IsChecked);
    }

    [TestMethod]
    public async Task ReadOnlyCheckBoxIgnoresClick()
    {
        var mockThemeService = new Mock<IThemeService>();
        var checkBox = new CheckBox(mockThemeService.Object)
        {
            Label = "Label",
            IsChecked = false,
            IsReadOnly = true,
            Position = new Position(0, 0),
            ViewSize = new Size(25, 1)
        };

        await checkBox.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 0), LeftClicked = true });
        Assert.IsFalse(checkBox.IsChecked);
    }

    [TestMethod]
    public async Task Draw()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var checkBox = new CheckBox(mockThemeService.Object)
        {
            Label = "Label",
            IsChecked = false,
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        checkBox.Draw(buffer);
        Assert.AreEqual("□ Label   " + Environment.NewLine, buffer.ContentAsString);
        
        await checkBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        Assert.IsTrue(checkBox.IsChecked);
        checkBox.Draw(buffer);
        Assert.AreEqual("▣ Label   " + Environment.NewLine, buffer.ContentAsString);
    }

    [TestMethod]
    public async Task RightClickDoesNotToggleCheckBox()
    {
        var mockThemeService = new Mock<IThemeService>();
        var checkBox = new CheckBox(mockThemeService.Object)
        {
            Label = "Label",
            Position = new Position(0, 0),
            ViewSize = new Size(25, 1)
        };

        await checkBox.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 0), RightClicked = true });

        Assert.IsFalse(checkBox.IsChecked);
    }
}
