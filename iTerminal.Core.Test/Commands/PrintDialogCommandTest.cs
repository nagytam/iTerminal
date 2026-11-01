using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Printing;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class PrintDialogCommandTest
{
    private static PrintDialogCommand CommandOn(string? path, bool focused = true)
    {
        var model = new Mock<IContentModel>();
        if (path is not null)
        {
            var entry = new Mock<IEntry>();
            entry.SetupGet(e => e.DisplayName).Returns(Path.GetFileName(path));
            entry.SetupGet(e => e.Url).Returns(new Url(path));
            model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
            // The drive says the row exists, whatever kind of drive it is: the gate under test is the URL's own kind.
            model.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns(!Directory.Exists(path));
            model.Setup(m => m.ExistsFolder(It.IsAny<Url>())).Returns(Directory.Exists(path));
        }

        var service = new Mock<IContentModelService>();
        // Inside a dialog nothing has focus: the last panel is the one to ask.
        service.Setup(s => s.GetFocusedModel()).Returns(focused ? model.Object : null);
        service.Setup(s => s.GetLastContentModel()).Returns(model.Object);
        return new PrintDialogCommand(
            new PrintDialog(Mock.Of<IPrintService>(), Mock.Of<ISettingsService>(), UiTestHelper.TestTheme()),
            service.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ItIsBoundToCtrlP_AndNamed()
    {
        var command = CommandOn(null);

        Assert.AreEqual(new Key(ConsoleKey.P, false, false, true), command.Key);
        Assert.AreEqual("Print", command.Title);
        Assert.IsFalse(string.IsNullOrWhiteSpace(command.Description));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ItAlwaysRuns_BecauseWithoutAFileItIsTheQueueManager()
    {
        using var folder = new TempFolder();
        var file = folder.File("a.txt");

        Assert.IsTrue(CommandOn(file).CanExecute());
        Assert.IsTrue(CommandOn(folder.Path).CanExecute(), "a folder row");
        Assert.IsTrue(CommandOn(null).CanExecute(), "no row at all");
        Assert.IsTrue(CommandOn("memdrive://docs/a.txt").CanExecute(), "another drive");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnlyALocalFileIsPrintable()
    {
        using var folder = new TempFolder();
        var file = folder.File("a.txt");

        Assert.IsTrue(CommandOn(file).HasPrintableFile);
        Assert.IsTrue(CommandOn(file, focused: false).HasPrintableFile, "inside a dialog the last panel answers");
        Assert.IsFalse(CommandOn(folder.Path).HasPrintableFile);
        Assert.IsFalse(CommandOn("memdrive://docs/a.txt").HasPrintableFile);
        Assert.IsFalse(CommandOn(null).HasPrintableFile);
    }

    private sealed class TempFolder : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("iTerminal.PrintCommand.");

        public string Path => _directory.FullName;

        public string File(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            System.IO.File.WriteAllText(path, "x");
            return path;
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
