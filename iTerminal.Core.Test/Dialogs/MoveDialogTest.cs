using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls.Buttons;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>
/// What sets the Move dialog apart from the other transfer dialog: its button and its hot key.
/// The shared layout and behaviour are covered by <c>TransferDialogTest</c>.
/// </summary>
[TestClass]
public class MoveDialogTest
{
    private static MoveDialog Create() =>
        new(null!, Mock.Of<iConsole.Interfaces.IContentModelService>(), null!, null!, null!, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(), null!, null!, null!, null!);

    [Timeout(60_000)]
    [TestMethod]
    public void ThePrimaryButtonIsNamedForTheOperation()
    {
        var buttons = Create().Controls.AsSnapshot().OfType<Button>().ToList();

        var primary = buttons.Single(b => b.Title == "Move");
        Assert.AreEqual('M', primary.HotKeyChar);
        Assert.IsTrue(buttons.Any(b => b.Title == "Cancel"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheTitleNamesTheOperation()
    {
        Assert.AreEqual("Move", Create().Title);
    }
}
