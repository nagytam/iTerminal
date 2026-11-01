using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Torrents;
using iTerminal.Core.Test.Dialogs.Torrents;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class TorrentManagerDialogCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ItIsBoundToCtrlShiftF1()
    {
        var dialog = new TorrentManagerDialog(new TorrentManagerDialogTest.FakeTorrents(), Mock.Of<IContentModelService>(),
            iTerminal.Core.Test.Helper.UiTestHelper.TestTheme());

        var command = new TorrentManagerDialogCommand(dialog);

        Assert.AreEqual("BitTorrent", command.Title);
        Assert.AreEqual(new Key(ConsoleKey.F1, true, false, true), command.Key);
    }
}
