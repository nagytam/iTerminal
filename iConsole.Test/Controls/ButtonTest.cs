using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ButtonTest
{
    [TestMethod]
    public async Task ButtonHandlesKeyEvents()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockCommand = new Mock<ICommand>();
        mockCommand.Setup(x => x.CanExecute()).Returns(true);
        var button = new Button("Title", mockCommand.Object, mockThemeService.Object)
        {
            IsEnabled = true,
            ViewSize = new Size(25, 1)
        };
        button.SetFocus();
        mockCommand.Verify(x => x.RunAsync(It.IsAny<object?>()), Times.Never);
        await button.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        mockCommand.Verify(x => x.CanExecute(), Times.AtLeastOnce);
        mockCommand.Verify(x => x.RunAsync(It.IsAny<object?>()), Times.Once);
    }

    [TestMethod]
    public void Draw()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var mockCommand = new Mock<ICommand>();
        var button = new Button("Title", mockCommand.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        button.Draw(buffer);
        Assert.AreEqual("[ Title  ]" + Environment.NewLine, buffer.ContentAsString);
    }
}
