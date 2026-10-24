using iConsole.Controls.Buttons;
using iConsole.Model;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Data;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Commands;

/// <summary>Enter in the Delete dialog runs the delete on the focused panel; with no panel it says so instead of doing nothing.</summary>
[TestClass]
public class DeleteCommandTest
{
    private static IThemeService Theme()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    [TestMethod]
    public async Task Enter_WithNoPanel_SaysSoOnTheDialog()
    {
        var theme = Theme();
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetLastContentModel()).Returns((IContentModel?)null);
        var taskManager = new Mock<TaskManager>();
        var progressWindowFactory = new ProgressWindowFactory(theme);
        var errorPromptService = Mock.Of<IErrorPromptService>();
        var resolver = new FileSystemResolver(
            Mock.Of<IFileSystemService>(),
            Mock.Of<IMemoryDriveService>(),
            Mock.Of<ISshFileSystemService>(),
            Mock.Of<IArchiveFileSystemService>(),
            contentModelService.Object);
        var command = new DeleteCommand(contentModelService.Object, taskManager.Object, progressWindowFactory, errorPromptService, resolver);
        var dialog = new DeleteDialog(
            contentModelService.Object,
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            theme,
            taskManager.Object,
            progressWindowFactory,
            errorPromptService,
            Mock.Of<IFileSystemService>(),
            Mock.Of<IMemoryDriveService>(),
            Mock.Of<ISshFileSystemService>(),
            Mock.Of<IArchiveFileSystemService>());
        dialog.AddControl(new Button("Delete", command, theme));

        await command.ExecuteAsync(null);

        Assert.AreEqual("There is no panel to delete from.", dialog.ErrorMessage.Text);
    }
}
