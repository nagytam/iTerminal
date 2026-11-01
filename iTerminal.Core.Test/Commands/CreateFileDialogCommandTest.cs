using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Shift+F4 opens the Create file dialog, seeded with the folder the panel shows.</summary>
[TestClass]
public class CreateFileDialogCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Key_IsShiftF4()
    {
        var command = new CreateFileDialogCommand(null!, Mock.Of<IContentModelService>());

        Assert.AreEqual(ConsoleKey.F4, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Shift);
        Assert.IsFalse(command.Key.Control);
        Assert.IsFalse(command.Key.Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Execute_OpensTheDialogInTheCurrentFolder()
    {
        var theme = UiTestHelper.TestTheme();
        var dialog = new CreateFileDialog(Mock.Of<IContentModelService>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), theme);
        var folder = new iConsole.Data.Url(Path.GetTempPath());
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.Url).Returns(folder);
        var command = new CreateFileDialogCommand(dialog, contentModelService.Object)
        {
            Control = new Control(theme),
        };

        await command.ExecuteAsync(null);

        Assert.AreEqual(folder.FullUrl, dialog.BaseUrl.FullUrl);
    }
}
