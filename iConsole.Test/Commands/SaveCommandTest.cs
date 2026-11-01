using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Commands;
using iConsole.Interfaces;
using iConsole.Dialogs;
using iConsole.Model;

namespace iConsole.Commands.Test;

[TestClass]
public class SaveCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_ShouldWriteFileAndClearDirty()
    {
        var textModel = new TextModel("line1");
        var cms = new Mock<IContentModelService>();
        var contentModel = new Mock<iConsole.Interfaces.IContentModel>();
        contentModel.Setup(cm => cm.WriteFileAsync(It.IsAny<iConsole.Data.Url>(), It.IsAny<System.IO.MemoryStream>())).Returns(Task.CompletedTask).Verifiable();
        cms.Setup(c => c.GetLastContentModel()).Returns(contentModel.Object);

        var theme = new Moq.Mock<iConsole.Services.IThemeService>();
        theme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        // Construct required services for dialog
        var fileToText = Mock.Of<iConsole.Services.IFileToTextConverterService>();
        var beep = Mock.Of<iConsole.Services.IBeepService>();
        var dt = Mock.Of<iConsole.Services.IDateTimeProviderService>();
        var dlg = new TextFileEditorDialog(Mock.Of<iConsole.Interfaces.IContentModelService>(), fileToText, beep, dt, theme.Object);
        dlg.Url = new iConsole.Data.Url("file://test.txt");
        dlg.TextEditorControl.IsDirty = true;

        var cmd = new SaveCommand(textModel, cms.Object);
        cmd.Control = dlg.TextEditorControl;

        await cmd.ExecuteAsync(null);

        contentModel.Verify(cm => cm.WriteFileAsync(It.IsAny<iConsole.Data.Url>(), It.IsAny<System.IO.MemoryStream>()), Times.Once);
        Assert.IsFalse(dlg.TextEditorControl.IsDirty);
    }
}

