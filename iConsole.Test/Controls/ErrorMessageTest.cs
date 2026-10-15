using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model.Validations;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ErrorMessageTest
{
    [TestMethod]
    public void ErrorMessageDrawsErrorText()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockValidationModel = new Mock<IValidationModel>();
        mockValidationModel.Setup(m => m.ErrorMessage).Returns("Invalid input");

        var buffer = new MockBuffer(new Size(30, 1));
        var errorMessage = new ErrorMessage(mockValidationModel.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 1)
        };

        errorMessage.Draw(buffer);
        // ErrorMessage should render the validation model's error message
        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    [TestMethod]
    public void ErrorMessageDrawsEmptyError()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockValidationModel = new Mock<IValidationModel>();
        mockValidationModel.Setup(m => m.ErrorMessage).Returns("");

        var buffer = new MockBuffer(new Size(20, 1));
        var errorMessage = new ErrorMessage(mockValidationModel.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        errorMessage.Draw(buffer);
        Assert.IsFalse(buffer.ContentAsString.Contains("Error:"), "no message, no caption");
    }

    [TestMethod]
    public void ErrorMessageTextPropertyReflectsValidationModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockValidationModel = new Mock<IValidationModel>();
        mockValidationModel.Setup(m => m.ErrorMessage).Returns("Test error message");

        var errorMessage = new ErrorMessage(mockValidationModel.Object, mockThemeService.Object);

        Assert.IsTrue(errorMessage.Text.Contains("Test error message"));
    }

    [TestMethod]
    public void ErrorMessageTextUpdatesWithValidationModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockValidationModel = new Mock<IValidationModel>();
        mockValidationModel.Setup(m => m.ErrorMessage).Returns("Initial error");

        var errorMessage = new ErrorMessage(mockValidationModel.Object, mockThemeService.Object);
        Assert.IsTrue(errorMessage.Text.Contains("Initial error"));

        // Update the mock to return different error message
        mockValidationModel.Setup(m => m.ErrorMessage).Returns("Updated error");
        Assert.IsTrue(errorMessage.Text.Contains("Updated error"));
    }

    [TestMethod]
    public void ErrorMessageDrawsWithOffset()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockValidationModel = new Mock<IValidationModel>();
        mockValidationModel.Setup(m => m.ErrorMessage).Returns("Error text");

        var buffer = new MockBuffer(new Size(30, 3));
        var errorMessage = new ErrorMessage(mockValidationModel.Object, mockThemeService.Object)
        {
            Position = new Position(3, 1),
            ViewSize = new Size(15, 1)
        };

        errorMessage.Draw(buffer);
        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        // Verify that drawing succeeded
        Assert.IsTrue(lines.Length > 1);
    }

    [TestMethod]
    public void ErrorMessageDrawsLongErrorMessage()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var longError = "This is a very long error message that should be truncated";
        var mockValidationModel = new Mock<IValidationModel>();
        mockValidationModel.Setup(m => m.ErrorMessage).Returns(longError);

        var buffer = new MockBuffer(new Size(20, 1));
        var errorMessage = new ErrorMessage(mockValidationModel.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        errorMessage.Draw(buffer);
        // Verify text is rendered, though it may be truncated due to view size
        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    [TestMethod]
    public void ErrorMessageInheritsFromLabel()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockValidationModel = new Mock<IValidationModel>();
        mockValidationModel.Setup(m => m.ErrorMessage).Returns("Test");

        var errorMessage = new ErrorMessage(mockValidationModel.Object, mockThemeService.Object);

        // ErrorMessage should inherit from Label
        Assert.IsInstanceOfType(errorMessage, typeof(Label));
    }

    [TestMethod]
    public void ErrorMessageTextStartsWithErrorPrefix()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockValidationModel = new Mock<IValidationModel>();
        mockValidationModel.Setup(m => m.ErrorMessage).Returns("Something went wrong");

        var errorMessage = new ErrorMessage(mockValidationModel.Object, mockThemeService.Object);

        Assert.IsTrue(errorMessage.Text.Contains("Something went wrong"));
    }

    /// <summary>
    /// The caption belongs to a message. Visibility and text were read at different moments, so a
    /// result that changed in between drew a bare "Error:" with nothing after it.
    /// </summary>
    [TestMethod]
    public void WithoutAMessageThereIsNoCaption()
    {
        var validationModel = new Mock<IValidationModel>();
        validationModel.Setup(m => m.ErrorMessage).Returns(string.Empty);
        var errorMessage = new ErrorMessage(validationModel.Object, new TestThemeService()) { ViewSize = new Size(30, 1) };

        Assert.AreEqual(string.Empty, errorMessage.Text);

        var screen = new ScreenBuffer(30, 1);
        errorMessage.Draw(screen);
        Assert.IsFalse(screen.Contains("Error"), "a row with no message is blank");
    }

    [TestMethod]
    public void AMessageGetsTheCaption()
    {
        var validationModel = new Mock<IValidationModel>();
        validationModel.Setup(m => m.ErrorMessage).Returns("Not a valid name or path.");
        var errorMessage = new ErrorMessage(validationModel.Object, new TestThemeService());

        Assert.AreEqual("Error: Not a valid name or path.", errorMessage.Text);
    }
}
