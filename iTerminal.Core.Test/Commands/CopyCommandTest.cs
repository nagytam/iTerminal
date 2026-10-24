using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Commands.Test;

/// <summary>The action behind the Copy dialog runs only from that dialog; with nothing attached it does nothing and does not throw.</summary>
[TestClass]
public class CopyCommandTest
{
    [TestMethod]
    public async Task ExecuteWithoutADialog_DoesNothing()
    {
        var theme = TestTheme();
        var contentModelService = Mock.Of<IContentModelService>();
        var resolver = new FileSystemResolver(
            Mock.Of<IFileSystemService>(),
            Mock.Of<IMemoryDriveService>(),
            Mock.Of<ISshFileSystemService>(),
            Mock.Of<IArchiveFileSystemService>(),
            contentModelService);
        var command = new CopyCommand(
            new Mock<TaskManager>().Object,
            new ProgressWindowFactory(theme),
            Mock.Of<IErrorPromptService>(),
            resolver,
            contentModelService);

        await command.ExecuteAsync(null);
    }
}
