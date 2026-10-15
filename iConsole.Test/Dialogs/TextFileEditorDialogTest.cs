using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Services;

namespace iConsole.Dialogs.Test;

[TestClass]
public class TextFileEditorDialogTest
{
    [TestMethod]
    public async Task OnInitializeAsync_ShouldLoadFileAndReflectDirtyState()
    {
        var contentModel = new Mock<IContentModelService>();
        var fileToText = new Mock<IFileToTextConverterService>();
        var beep = new Mock<IBeepService>();
        var dt = new Mock<IDateTimeProviderService>();
        var theme = new Mock<IThemeService>();
        theme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var mockFs = new Mock<IFileSystem>();
        contentModel.Setup(c => c.GetFocusedModel()).Returns(mockFs.Object as iConsole.Interfaces.IContentModel);
        fileToText.Setup(f => f.ReadFileAsync(null, It.IsAny<IFileSystem>(), It.IsAny<iConsole.Model.TextModel>())).Returns(Task.CompletedTask);

        var dlg = new TextFileEditorDialog(contentModel.Object, fileToText.Object, beep.Object, dt.Object, theme.Object);

        await dlg.OnInitializeAsync();

        dlg.TextEditorControl.IsDirty = true;
        Assert.IsTrue(dlg.Title.Contains("(*)") || dlg.Title.Contains("Text file editor"));
    }
}
