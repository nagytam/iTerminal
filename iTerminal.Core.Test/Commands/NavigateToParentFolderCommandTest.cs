using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class NavigateToParentFolderCommandTest
{
    /// <summary>
    /// Presses Ctrl+PageUp through the ContentBrowser command pipeline and verifies the content
    /// model is navigated to the parent folder.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task CtrlPageUp_NavigatesToParentFolder()
    {
        // Arrange — real folder hierarchy in temp
        var parentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var childPath  = Path.Combine(parentPath, "child");
        ContentModelFileSystem? contentModel = null;
        Directory.CreateDirectory(childPath);

        try
        {
            var settingsService = new Mock<ISettingsService>(MockBehavior.Loose);
            settingsService.Setup(s => s.Get(SettingsService.FOLDER_HISTORY)).Returns((string?)null);

            var frequentExecutionGuardService = new Mock<IFrequentExecutionGuardService>();
            frequentExecutionGuardService
                .Setup(g => g.Execute(It.IsAny<Action>()))
                .Callback<Action>(action => action());

            var fileSystemService = new Mock<IFileSystemService>();
            fileSystemService
                .Setup(s => s.ExistsFolder(It.IsAny<Url>()))
                .Returns<Url>(url => Directory.Exists(url.Path));

            var taskManager        = new TaskManager();
            var memoryCacheService = new MemoryCacheService();
            var folderHistoryService = new FolderHistoryService(settingsService.Object);

            contentModel = new ContentModelFileSystem(
                name:                        "LeftPanel",
                taskManager:                 taskManager,
                settingsService:             settingsService.Object,
                folderHistoryService:        folderHistoryService,
                fileSystemService:           fileSystemService.Object,
                frequentExecutionGuardService: frequentExecutionGuardService.Object,
                memoryCacheService:          memoryCacheService,
                logger:                      Mock.Of<Serilog.ILogger>());

            contentModel.ViewSize = new iConsole.Data.Size(80, 20);
            contentModel.SetUrl(new Url(childPath));

            var contentModelService = new Mock<IContentModelService>(MockBehavior.Loose);

            // Capture the URL passed to SetUrlAsync(name, url)
            Url? navigatedUrl = null;
            contentModelService
                .Setup(s => s.SetUrlAsync("LeftPanel", It.IsAny<Url>()))
                .Callback<string, Url>((_, url) => navigatedUrl = url)
                .Returns(Task.CompletedTask);

            var command = new NavigateToParentFolderCommand(contentModel, contentModelService.Object);

            // Act — simulate pressing Ctrl+PageUp
            var keyEvent = new KeyEvent('\0', ConsoleKey.PageUp, shift: false, alt: false, control: true);
            var handled  = await command.HandleKeyEventAsync(keyEvent);

            // Assert
            Assert.IsTrue(handled, "NavigateToParentFolderCommand should report the key as handled.");
            Assert.IsNotNull(navigatedUrl, "SetUrlAsync should have been called with the parent URL.");
            Assert.IsTrue(
                navigatedUrl!.FullUrl.Contains(Path.GetFileName(parentPath)),
                $"Expected parent URL to contain '{Path.GetFileName(parentPath)}', got '{navigatedUrl.FullUrl}'.");
            Assert.IsFalse(
                navigatedUrl.FullUrl.Contains("child"),
                $"Parent URL should not contain 'child', got '{navigatedUrl.FullUrl}'.");
        }
        finally
        {
            contentModel?.Dispose();
            if (Directory.Exists(parentPath))
                Directory.Delete(parentPath, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that pressing Ctrl+PageUp in ContentBrowser navigates to the parent folder.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task CtrlPageUp_ThroughContentBrowser_NavigatesToParentFolder()
    {
        // Arrange
        var parentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var childPath  = Path.Combine(parentPath, "child");
        ContentModelFileSystem? contentModel = null;
        Directory.CreateDirectory(childPath);

        try
        {
            var settingsService = new Mock<ISettingsService>(MockBehavior.Loose);
            settingsService.Setup(s => s.Get(SettingsService.FOLDER_HISTORY)).Returns((string?)null);

            var frequentExecutionGuardService = new Mock<IFrequentExecutionGuardService>();
            frequentExecutionGuardService
                .Setup(g => g.Execute(It.IsAny<Action>()))
                .Callback<Action>(action => action());

            var fileSystemService = new Mock<IFileSystemService>();
            fileSystemService
                .Setup(s => s.ExistsFolder(It.IsAny<Url>()))
                .Returns<Url>(url => Directory.Exists(url.Path));

            var taskManager        = new TaskManager();
            var memoryCacheService = new MemoryCacheService();
            var folderHistoryService = new FolderHistoryService(settingsService.Object);

            contentModel = new ContentModelFileSystem(
                name:                        "LeftPanel",
                taskManager:                 taskManager,
                settingsService:             settingsService.Object,
                folderHistoryService:        folderHistoryService,
                fileSystemService:           fileSystemService.Object,
                frequentExecutionGuardService: frequentExecutionGuardService.Object,
                memoryCacheService:          memoryCacheService,
                logger:                      Mock.Of<Serilog.ILogger>());

            contentModel.ViewSize = new iConsole.Data.Size(80, 20);
            contentModel.SetUrl(new Url(childPath));

            // Use a mocked ContentModelService that captures the navigation call
            var realContentModelService = new Mock<IContentModelService>(MockBehavior.Loose);

            Url? navigatedUrl = null;
            realContentModelService
                .Setup(s => s.SetUrlAsync("LeftPanel", It.IsAny<Url>()))
                .Callback<string, Url>((_, url) => navigatedUrl = url)
                .Returns(Task.CompletedTask);

            var terminalModel = new Mock<iTerminal.Core.Model.TerminalModel>(
                MockBehavior.Loose,
                Mock.Of<iConsole.Tasks.TaskManager>(),
                Mock.Of<IContentModelService>()).Object;

            var browser = new ContentBrowser(
                name:                 "LeftPanel",
                contentModelService:  realContentModelService.Object,
                terminalModel:        terminalModel,
                taskManager:          taskManager,
                themeService:         new ThemeService());

            browser.ContentModel = contentModel;
            browser.ViewSize     = new iConsole.Data.Size(80, 20);
            browser.SetFocus();
            await browser.OnInitializeAsync();

            // Act — Ctrl+PageUp
            var keyEvent = new KeyEvent('\0', ConsoleKey.PageUp, shift: false, alt: false, control: true);
            var handled  = await browser.HandleKeyEventAsync(keyEvent);

            // Assert
            Assert.IsTrue(handled, "ContentBrowser should report Ctrl+PageUp as handled.");
            Assert.IsNotNull(navigatedUrl,
                "Ctrl+PageUp in ContentBrowser should call SetUrlAsync (navigate to parent folder).");
            Assert.IsFalse(
                navigatedUrl!.FullUrl.Contains("child"),
                $"ParentUrl must not still point to 'child'; got '{navigatedUrl.FullUrl}'.");
        }
        finally
        {
            contentModel?.Dispose();
            if (Directory.Exists(parentPath))
                Directory.Delete(parentPath, recursive: true);
        }
    }
}
