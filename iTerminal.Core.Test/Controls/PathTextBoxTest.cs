using iConsole.Data;
using iConsole.Display;
using iConsole.Model.Validations;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Controls;
using iTerminal.Core.Model.Validations;
using Moq;

namespace iTerminal.Core.Test.Controls;

[TestClass]
public class PathTextBoxTest
{
    [TestMethod]
    public void TextBoxValidator()
    {
        var mockBeepService = new Mock<IBeepService>();
        var mockDateTimeProviderService = new Mock<DateTimeProviderService>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(12, 1));
        var textBox = new PathTextBox(mockBeepService.Object, mockDateTimeProviderService.Object, mockThemeService.Object)
        {
            Label = "Filename",
            Text = "ABC",
            Position = new Position(0, 0),
            ViewSize = new Size(12, 1),
        };
        textBox.ValidationModel = new PathValidationModel(textBox.TextModel, mockDateTimeProviderService.Object);
        textBox.ValidationModel.Validate(DateTime.Now);
        Assert.IsTrue(textBox.ValidationModel.IsValid);
    }
}
