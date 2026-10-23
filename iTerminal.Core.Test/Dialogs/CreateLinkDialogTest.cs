using iConsole.Controls;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Links;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Ctrl+L: the link's path and kind, the options a folder cannot have, and failures that stay in the dialog in words.</summary>
[TestClass]
public class CreateLinkDialogTest
{
    private static readonly string Target = Path.Combine(Path.GetTempPath(), "left", "report.pdf");
    private static readonly string Link = Path.Combine(Path.GetTempPath(), "right", "report.pdf");

    private sealed class Rig
    {
        public required CreateLinkDialog Dialog { get; init; }

        public required Control Root { get; init; }

        public required Mock<ILinkService> Links { get; init; }

        public required Mock<IContentModel> Local { get; init; }

        public required Mock<IContentModel> Remote { get; init; }
    }

    private static async Task<Rig> OpenAsync(bool folder = false, Exception? failure = null)
    {
        Mock<ILinkService> links = new();
        if (failure is not null)
        {
            links.Setup(l => l.CreateAsync(It.IsAny<LinkKind>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).ThrowsAsync(failure);
        }
        else
        {
            links.Setup(l => l.CreateAsync(It.IsAny<LinkKind>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(Task.CompletedTask);
        }

        Mock<IContentModel> local = new();
        local.SetupGet(m => m.Url).Returns(new Url(Path.GetTempPath()));
        Mock<IContentModel> remote = new();
        remote.SetupGet(m => m.Url).Returns(new Url("memdrive://x/"));
        Mock<IContentModelService> panels = new();
        ConcurrentList<IContentModel> list = [];
        list.Add(local.Object);
        list.Add(remote.Object);
        panels.SetupGet(p => p.ContentModels).Returns(list);
        CreateLinkDialog dialog = new(links.Object, panels.Object, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load(Target, folder, Link);
        Control root = await ShowAsync(dialog);
        return new Rig { Dialog = dialog, Root = root, Links = links, Local = local, Remote = remote };
    }

    [TestMethod]
    public async Task ItShowsTheLinkPath_TheTarget_AndTheOptions()
    {
        Rig rig = await OpenAsync();

        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Create link");
        AssertScreenContains(screen, Path.GetFileName(Link));
        AssertScreenContains(screen, "to   ");
        AssertScreenContains(screen, "Hard link");
        AssertScreenContains(screen, "Relative target");
        Assert.AreEqual(Link, rig.Dialog.LinkPath);
        Assert.AreEqual(Target, rig.Dialog.TargetPath);
    }

    [TestMethod]
    public async Task AFolder_CannotBeHardLinked()
    {
        Rig rig = await OpenAsync(folder: true);

        rig.Dialog.IsHardLink = true;

        Assert.IsFalse(rig.Dialog.IsHardLink);
        Assert.IsTrue(rig.Dialog.TargetIsFolder);
    }

    [TestMethod]
    public async Task AFile_CanBeHardLinked()
    {
        Rig rig = await OpenAsync();

        rig.Dialog.IsHardLink = true;

        Assert.IsTrue(rig.Dialog.IsHardLink);
    }

    [TestMethod]
    public async Task Create_MakesASymbolicLink_ByDefault_ClosesAndRereadsLocalPanelsOnly()
    {
        Rig rig = await OpenAsync();

        Assert.IsTrue(await rig.Dialog.CreateAsync());

        rig.Links.Verify(l => l.CreateAsync(LinkKind.Symbolic, Link, Target, false), Times.Once);
        Assert.IsFalse(rig.Dialog.IsVisible);
        rig.Local.Verify(m => m.Refresh(true), Times.Once);
        rig.Remote.Verify(m => m.Refresh(It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task TheOptions_ChooseAHardOrRelativeLink()
    {
        Rig rig = await OpenAsync();
        rig.Dialog.IsHardLink = true;
        rig.Dialog.IsRelative = true;

        await rig.Dialog.CreateAsync();

        rig.Links.Verify(l => l.CreateAsync(LinkKind.Hard, Link, Target, true), Times.Once);
    }

    [TestMethod]
    public async Task AFileUrlAndSurroundingSpaces_AreTidiedBeforeUse()
    {
        Rig rig = await OpenAsync();
        rig.Dialog.LinkPath = "  " + Container.FILE + Link + "  ";

        await rig.Dialog.CreateAsync();

        rig.Links.Verify(l => l.CreateAsync(LinkKind.Symbolic, Link, Target, false), Times.Once);
    }

    [TestMethod]
    public async Task AnEmptyPath_SaysWhereTheLinkMustGo_AndNothingIsCreated()
    {
        Rig rig = await OpenAsync();
        rig.Dialog.LinkPath = "   ";

        Assert.IsFalse(await rig.Dialog.CreateAsync());

        StringAssert.Contains(rig.Dialog.Error, "Type where the link goes.");
        Assert.IsTrue(rig.Dialog.IsVisible);
        rig.Links.Verify(l => l.CreateAsync(It.IsAny<LinkKind>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        AssertScreenContains(Render(rig.Root), "Error: Type where the link goes.");
    }

    [TestMethod]
    public async Task AFailure_StaysInTheDialog_InWords_WrappedOverSeveralRows()
    {
        string message = "A required privilege is not held by the client, so the symbolic link could not be made here; run as administrator or turn on developer mode.";
        Rig rig = await OpenAsync(failure: new UnauthorizedAccessException(message));

        Assert.IsFalse(await rig.Dialog.CreateAsync());

        Assert.IsTrue(rig.Dialog.IsVisible);
        StringAssert.Contains(rig.Dialog.Error, "Error: A required privilege");
        StringAssert.Contains(rig.Dialog.Error, "developer mode");
        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Error: A required privilege");
        AssertScreenContains(screen, "developer mode");
    }

    [TestMethod]
    public async Task EnterCreates_AndEscapeCloses()
    {
        Rig rig = await OpenAsync();

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await rig.Dialog.CreateTask;
        rig.Links.Verify(l => l.CreateAsync(LinkKind.Symbolic, Link, Target, false), Times.Once);

        Rig other = await OpenAsync();
        await other.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.IsFalse(other.Dialog.IsVisible);
        other.Links.Verify(l => l.CreateAsync(It.IsAny<LinkKind>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task LoadingAgain_ClearsTheOptionsAndTheError()
    {
        Rig rig = await OpenAsync();
        rig.Dialog.IsHardLink = true;
        rig.Dialog.IsRelative = true;
        rig.Dialog.LinkPath = string.Empty;
        await rig.Dialog.CreateAsync();

        rig.Dialog.Load(Target, true, Link);

        Assert.IsFalse(rig.Dialog.IsHardLink);
        Assert.IsFalse(rig.Dialog.IsRelative);
        Assert.AreEqual(string.Empty, rig.Dialog.Error);
    }

    [TestMethod]
    public void DefaultLinkPath_IsBesideTheTarget_WhenTheOtherPanelShowsTheSameFolderOrNone()
    {
        string folder = Path.GetDirectoryName(Target)!;

        Assert.AreEqual(Path.Combine(folder, "report - link.pdf"), CreateLinkDialog.DefaultLinkPath(Target, folder + Path.DirectorySeparatorChar));
        Assert.AreEqual(Path.Combine(folder, "report - link.pdf"), CreateLinkDialog.DefaultLinkPath(Target, null));
        Assert.AreEqual(Path.Combine(folder, "report - link.pdf"), CreateLinkDialog.DefaultLinkPath(Target, string.Empty));
        Assert.AreEqual(Path.Combine(folder, "docs - link"), CreateLinkDialog.DefaultLinkPath(Path.Combine(folder, "docs") + Path.DirectorySeparatorChar, null));
    }
}
