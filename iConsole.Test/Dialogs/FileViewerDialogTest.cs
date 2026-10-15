using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Services;
using Moq;
using System.Reflection;

namespace iConsole.Test.Dialogs;

[TestClass]
public class FileViewerDialogTest
{
    [TestMethod]
    public void Constructor_CreatesVisibleDialog()
    {
        FileViewerDialog dialog = new(
            Mock.Of<IContentModelService>(),
            Mock.Of<IFileToTextConverterService>(),
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService());

        Assert.IsNotNull(dialog);
        Assert.IsTrue(dialog.Controls.Count >= 2);
        Assert.IsNull(dialog.Url);
    }

    [TestMethod]
    public async Task CloseSelfCommand_ExecuteAsync_ClosesDialog()
    {
        FileViewerDialog dialog = new(
            Mock.Of<IContentModelService>(),
            Mock.Of<IFileToTextConverterService>(),
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService());
        Type commandType = typeof(FileViewerDialog).GetNestedType("CloseSelfCommand", BindingFlags.NonPublic)!;
        object command = Activator.CreateInstance(commandType, dialog)!;

        Task executeTask = (Task)commandType.GetMethod("ExecuteAsync")!.Invoke(command, [null])!;
        await executeTask;

        Assert.IsFalse(dialog.IsVisible);
    }

    [TestMethod]
    public async Task ToggleHexModeAsync_LoadsRawBytesAndSwitchesView()
    {
        Url url = new("file://C:/x/data.bin");
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns([0x41, 0x42, 0x00]);
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(url)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        FileViewerDialog dialog = new(
            models.Object,
            Mock.Of<IFileToTextConverterService>(),
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService());

        dialog.Url = url;
        Assert.IsFalse(dialog.IsHexMode);
        await dialog.ToggleHexModeAsync();

        Assert.IsTrue(dialog.IsHexMode);
        CollectionAssert.AreEqual(new byte[] { 0x41, 0x42, 0x00 }, dialog.HexViewer.Data);
        await dialog.ToggleHexModeAsync();
        Assert.IsFalse(dialog.IsHexMode);
    }
}
