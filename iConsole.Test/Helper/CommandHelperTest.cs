using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Helper;

[TestClass]
public class CommandHelperTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void AddVerticalScrollingCommands_AddsAllCommands()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var scrollableModel = new ScrollableModel();
        var control = new Label("Test", mockThemeService.Object);

        CommandHelper.AddVerticalScrollingCommands(control, scrollableModel, false);

        Assert.IsTrue(control.Commands.Count >= 4);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddVerticalScrollingCommands_WithHomeEnd_AddsMoreCommands()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var scrollableModel = new ScrollableModel();
        var control = new Label("Test", mockThemeService.Object);

        CommandHelper.AddVerticalScrollingCommands(control, scrollableModel, true);

        Assert.IsTrue(control.Commands.Count >= 6);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddVerticalScrollingCommands_NullControl_ThrowsException()
    {
        var scrollableModel = new ScrollableModel();

        try
        {
            CommandHelper.AddVerticalScrollingCommands(null!, scrollableModel, false);
            Assert.Fail("Expected ArgumentNullException");
        }
        catch (ArgumentNullException)
        {
            // Expected
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddHorizontalScrollingCommands_AddsAllCommands()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var scrollableModel = new ScrollableModel();
        var textModel = new TextModel();
        var selectionTextModel = new SelectionTextModel();
        var control = new Label("Test", mockThemeService.Object);

        CommandHelper.AddHorizontalScrollingCommands(control, scrollableModel, textModel, selectionTextModel, true);

        Assert.IsTrue(control.Commands.Count >= 8);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddHorizontalScrollingCommands_NullControl_ThrowsException()
    {
        var scrollableModel = new ScrollableModel();
        var textModel = new TextModel();
        var selectionTextModel = new SelectionTextModel();

        try
        {
            CommandHelper.AddHorizontalScrollingCommands(null!, scrollableModel, textModel, selectionTextModel, true);
            Assert.Fail("Expected ArgumentNullException");
        }
        catch (ArgumentNullException)
        {
            // Expected
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddHorizontalScrollingCommands_WithCursorDisabled_AddsCommands()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var scrollableModel = new ScrollableModel();
        var textModel = new TextModel();
        var selectionTextModel = new SelectionTextModel();
        var control = new Label("Test", mockThemeService.Object);

        CommandHelper.AddHorizontalScrollingCommands(control, scrollableModel, textModel, selectionTextModel, false);

        Assert.IsTrue(control.Commands.Count >= 8);
    }
}
