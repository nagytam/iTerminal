using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Streams;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class StreamsDialogCommandTest
{
    private static StreamsDialogCommand CommandOn(string? path, string? displayName = null, bool focused = true, bool driveObject = false)
    {
        var model = new Mock<IContentModel>();
        if (path is not null)
        {
            Mock<IEntry> entry = new();
            if (driveObject)
            {
                entry.As<iTerminal.Core.Model.ContentModels.IDriveObjectEntry>();
            }

            entry.SetupGet(e => e.DisplayName).Returns(displayName ?? Path.GetFileName(path));
            entry.SetupGet(e => e.Url).Returns(new Url(path));
            model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
            model.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns(true);
            model.Setup(m => m.ExistsFolder(It.IsAny<Url>())).Returns(false);
        }

        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(focused ? model.Object : null);
        service.Setup(s => s.GetLastContentModel()).Returns(model.Object);
        return new StreamsDialogCommand(new StreamsDialog(Mock.Of<IStreamAttributeService>(), UiTestHelper.TestTheme()), service.Object);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ItHasNoChord_AndIsNamed()
    {
        var command = CommandOn(null);

        Assert.IsNull(command.Key);
        Assert.AreEqual("Streams and Attributes", command.Title);
        Assert.IsFalse(string.IsNullOrWhiteSpace(command.Description));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ItActsOnALocalEntry_AlsoFromInsideADialog()
    {
        string file = Path.Combine(Path.GetTempPath(), "a.txt");

        Assert.IsTrue(CommandOn(file).CanExecute());
        Assert.IsTrue(CommandOn(file, focused: false).CanExecute(), "inside a dialog the last panel answers");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ItRefusesTheParentRow_AContainerRow_AnotherDrive_AndNoRow()
    {
        string file = Path.Combine(Path.GetTempPath(), "a.txt");

        Assert.IsFalse(CommandOn(file, displayName: "..").CanExecute(), "the .. row");
        Assert.IsFalse(CommandOn(file, driveObject: true).CanExecute(), "a container row");
        Assert.IsFalse(CommandOn("memdrive://docs/a.txt").CanExecute(), "another drive");
        Assert.IsFalse(CommandOn(null).CanExecute(), "nothing under the cursor");
    }
}
