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
}

