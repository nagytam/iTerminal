using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.FileTransfer;
using iTerminal.Core.Services.Sharing;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class FileTransferDialogCommandTest
{
    [TestMethod]
    public void CanExecute_WithNoPanelOrSelection_IsDisabled()
    {
        Mock<IFileShareService> service = new();
        service.Setup(s => s.State).Returns(FileShareState.Stopped);
        FileTransferDialog dialog = new(service.Object, TestTheme());
        FileTransferDialogCommand command = new(dialog, Mock.Of<IContentModelService>());

        Assert.IsFalse(command.CanExecute(), "nothing to share, so the menu entry is disabled");
    }
}
