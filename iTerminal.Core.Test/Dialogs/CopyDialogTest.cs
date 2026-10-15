using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls.Buttons;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>
/// What sets the Copy dialog apart from the other transfer dialog: its button and its hot key.
/// The shared layout and behaviour are covered by <c>TransferDialogTest</c>.
/// </summary>
[TestClass]
public class CopyDialogTest
{
    private static CopyDialog Create() =>
        new(null!, Mock.Of<iConsole.Interfaces.IContentModelService>(), null!, null!, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(), null!, null!, null!, null!);

    [TestMethod]
    public void ThePrimaryButtonIsNamedForTheOperation()
    {
        var buttons = Create().Controls.AsSnapshot().OfType<Button>().ToList();

        var primary = buttons.Single(b => b.Title == "Copy");
        Assert.AreEqual('y', primary.HotKeyChar);
        Assert.IsTrue(buttons.Any(b => b.Title == "Cancel"));
    }

    [TestMethod]
    public void TheTitleNamesTheOperation()
    {
        Assert.AreEqual("Copy", Create().Title);
    }
}
