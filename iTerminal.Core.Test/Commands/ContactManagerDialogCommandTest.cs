using iConsole.Data;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Contacts;
using iTerminal.Core.Model.Contacts;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class ContactManagerDialogCommandTest
{
    [TestMethod]
    public void ItIsBoundToCtrlShiftK()
    {
        var dialog = new ContactManagerDialog(new ContactStore(Mock.Of<ISettingsService>()), iTerminal.Core.Test.Helper.UiTestHelper.TestTheme());

        var command = new ContactManagerDialogCommand(dialog);

        Assert.AreEqual("Contacts", command.Title);
        Assert.AreEqual(new Key(ConsoleKey.K, true, false, true), command.Key);
    }
}
