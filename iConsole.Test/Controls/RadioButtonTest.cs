using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class RadioButtonTest
{
    private static RadioButton CreateControl(Mock<IThemeService>? themeService = null) =>
        new((themeService ?? new Mock<IThemeService>()).Object)
        {
            Label = "Option",
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

    [TestMethod]
    public async Task SpaceSelectsWhenFocused()
    {
        var control = CreateControl();
        control.SetFocus();

        await control.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.IsTrue(control.IsSelected);
    }

    [TestMethod]
    public async Task ClickSelects()
    {
        var control = CreateControl();

        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });

        Assert.IsTrue(control.IsSelected);
    }

    [TestMethod]
    public async Task ReadOnlyIgnoresClick()
    {
        var control = CreateControl();
        control.IsReadOnly = true;

        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });

        Assert.IsFalse(control.IsSelected);
    }

    [TestMethod]
    public async Task SelectRequestedIsRaised()
    {
        var control = CreateControl();
        var raised = 0;
        control.SelectRequested += (_, _) => raised++;

        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });

        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void DrawShowsGlyphForSelectionState()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var control = new RadioButton(themeService.Object)
        {
            Label = "Option",
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        control.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("○ Option"));

        control.IsSelected = true;
        control.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("◉ Option"));
    }
}
