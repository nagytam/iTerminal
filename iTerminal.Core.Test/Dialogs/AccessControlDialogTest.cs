using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.AccessControl;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The permissions dialog: the list as drawn, and each key's change reaching the service off the key handler.</summary>
[TestClass]
public class AccessControlDialogTest
{
    private static readonly string[] WindowsPresets = ["Full control", "Modify", "Read & execute", "Read", "Write"];
    private static readonly string TargetPath = Path.Combine(Path.GetTempPath(), "dir", "report.txt");

    private static AclInfo Info(
        bool isFolder = false,
        bool deny = true,
        bool inherits = true,
        string owner = "PC\\alice",
        string? group = null,
        string[]? presets = null,
        params AclEntry[] entries) =>
        new(TargetPath, isFolder, owner, group,
            entries.Length > 0 ? entries :
            [
                new AclEntry("PC\\alice", AclKind.Allow, "Full control", true, false),
                new AclEntry("BUILTIN\\Users", AclKind.Allow, "Read & execute", true, true, "folder, subfolders, files"),
            ],
            inherits, CanChangeOwner: true, CanChangeInheritance: deny, SupportsDeny: deny, presets ?? WindowsPresets);

    private sealed record Rig(AccessControlDialog Dialog, Mock<IAccessControlService> Service);

    private static async Task<Rig> OpenAsync(AclInfo info)
    {
        var service = new Mock<IAccessControlService>();
        service.Setup(s => s.Read(It.IsAny<string>())).Returns(info);
        var dialog = new AccessControlDialog(service.Object, TestTheme());
        dialog.Load(TargetPath);
        await ShowAsync(dialog);
        await dialog.PendingOperation;
        return new Rig(dialog, service);
    }

    [TestMethod]
    public async Task TheListShowsTheOwnerTheEntriesAndWhichAreInherited()
    {
        var rig = await OpenAsync(Info());

        var screen = Render(rig.Dialog);
        AssertScreenContains(screen, "Owner: PC\\alice");
        AssertScreenContains(screen, "Inheriting entries from the parent folder");
        AssertScreenContains(screen, "Full control");
        AssertScreenContains(screen, "BUILTIN\\Users");
        AssertScreenContains(screen, "inherited, folder, subfolders, files");
    }

    [TestMethod]
    public async Task APosixListShowsTheGroupAndNoDenyColumn()
    {
        var rig = await OpenAsync(Info(
            deny: false, inherits: false, owner: "alice", group: "staff", presets: ["rwx", "r-x", "r--"],
            entries: [new AclEntry("owner", AclKind.Allow, "rwx", true, false), new AclEntry("user:bob", AclKind.Allow, "r-- (effective r--)", true, false)]));

        var screen = Render(rig.Dialog);
        AssertScreenContains(screen, "Owner: alice   Group: staff");
        AssertScreenContains(screen, "user:bob");
        AssertScreenDoesNotContain(screen, "Allow ");
    }

    [TestMethod]
    public async Task AFailedReadIsSaidInTheFooter()
    {
        var service = new Mock<IAccessControlService>();
        service.Setup(s => s.Read(It.IsAny<string>())).Throws(new UnauthorizedAccessException("Attempted to perform an unauthorized operation."));
        var dialog = new AccessControlDialog(service.Object, TestTheme());
        dialog.Load(TargetPath);
        await ShowAsync(dialog);
        await dialog.PendingOperation;

        StringAssert.Contains(dialog.StatusForTest, "unauthorized");
    }

    [TestMethod]
    public async Task AddingAnEntryTypesTheNameAndCyclesTheRights()
    {
        var rig = await OpenAsync(Info());
        await rig.Dialog.HandleKeyEventAsync(Char('a'));
        foreach (var c in "Guests")
        {
            await rig.Dialog.HandleKeyEventAsync(Char(c));
        }

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t')); // Type
        await rig.Dialog.HandleKeyEventAsync(Char(' ')); // Deny
        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t')); // Rights: starts on Read
        await rig.Dialog.HandleKeyEventAsync(Char(' ')); // Write
        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await rig.Dialog.PendingOperation;

        rig.Service.Verify(s => s.Set(TargetPath, "Guests", AclKind.Deny, "Write", false), Times.Once);
        rig.Service.Verify(s => s.Read(It.IsAny<string>()), Times.Exactly(2)); // the list is read again after the change
        Assert.AreEqual("List", rig.Dialog.ModeForTest);
    }

    [TestMethod]
    public async Task AFolderEntryIsInheritedByChildrenUnlessSwitchedOff()
    {
        var rig = await OpenAsync(Info(isFolder: true));
        await rig.Dialog.HandleKeyEventAsync(Char('a'));
        foreach (var c in "Guests")
        {
            await rig.Dialog.HandleKeyEventAsync(Char(c));
        }

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await rig.Dialog.PendingOperation;

        rig.Service.Verify(s => s.Set(TargetPath, "Guests", AclKind.Allow, "Read", true), Times.Once);
    }

    [TestMethod]
    public async Task EnterChangesTheRightsOfTheChosenEntry()
    {
        var rig = await OpenAsync(Info());

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r')); // opens on Rights, "Full control"
        await rig.Dialog.HandleKeyEventAsync(Char(' ')); // Modify
        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await rig.Dialog.PendingOperation;

        rig.Service.Verify(s => s.Set(TargetPath, "PC\\alice", AclKind.Allow, "Modify", false), Times.Once);
    }

    [TestMethod]
    public async Task AnInheritedEntryIsNotEditedOrRemovedHere()
    {
        var rig = await OpenAsync(Info());
        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        StringAssert.Contains(rig.Dialog.StatusForTest, "inherited");
        Assert.AreEqual("List", rig.Dialog.ModeForTest);

        await rig.Dialog.HandleKeyEventAsync(Char('d'));
        await rig.Dialog.HandleKeyEventAsync(Char('y'));
        rig.Service.Verify(s => s.Remove(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AclKind>()), Times.Never);
    }

    [TestMethod]
    public async Task RemovingAskesFirstAndYRemoves()
    {
        var rig = await OpenAsync(Info());

        await rig.Dialog.HandleKeyEventAsync(Char('d'));
        rig.Service.Verify(s => s.Remove(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AclKind>()), Times.Never);
        await rig.Dialog.HandleKeyEventAsync(Char('y'));
        await rig.Dialog.PendingOperation;

        rig.Service.Verify(s => s.Remove(TargetPath, "PC\\alice", AclKind.Allow), Times.Once);
    }

    [TestMethod]
    public async Task AnyOtherKeyCancelsARemoval()
    {
        var rig = await OpenAsync(Info());

        await rig.Dialog.HandleKeyEventAsync(Char('d'));
        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await rig.Dialog.HandleKeyEventAsync(Char('y'));

        rig.Service.Verify(s => s.Remove(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AclKind>()), Times.Never);
    }

    [TestMethod]
    public async Task IFlipsInheritanceWhereTheSystemHasIt()
    {
        var rig = await OpenAsync(Info(inherits: true));

        await rig.Dialog.HandleKeyEventAsync(Char('i'));
        await rig.Dialog.PendingOperation;

        rig.Service.Verify(s => s.SetInheritance(TargetPath, false), Times.Once);
    }

    [TestMethod]
    public async Task AChangeThatFailsShowsTheReasonAndStillRereadsTheList()
    {
        var rig = await OpenAsync(Info());
        rig.Service.Setup(s => s.Set(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AclKind>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new ArgumentException("\"Nobody\" is not a user or group this computer knows."));

        await rig.Dialog.HandleKeyEventAsync(Char('a'));
        foreach (var c in "Nobody")
        {
            await rig.Dialog.HandleKeyEventAsync(Char(c));
        }

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await rig.Dialog.PendingOperation;

        StringAssert.Contains(rig.Dialog.StatusForTest, "not a user or group");
        rig.Service.Verify(s => s.Read(It.IsAny<string>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task OChangesTheOwnerAndTheGroupWhenTheSystemHasOne()
    {
        var rig = await OpenAsync(Info(deny: false, inherits: false, owner: "alice", group: "staff", presets: ["rwx", "r--"],
            entries: [new AclEntry("owner", AclKind.Allow, "rwx", true, false)]));

        await rig.Dialog.HandleKeyEventAsync(Char('o'));
        for (var i = 0; i < "alice".Length; i++)
        {
            await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace, '\b'));
        }

        foreach (var c in "bob")
        {
            await rig.Dialog.HandleKeyEventAsync(Char(c));
        }

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t')); // Group
        for (var i = 0; i < "staff".Length; i++)
        {
            await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace, '\b'));
        }

        foreach (var c in "ops")
        {
            await rig.Dialog.HandleKeyEventAsync(Char(c));
        }

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await rig.Dialog.PendingOperation;

        rig.Service.Verify(s => s.SetOwner(TargetPath, "bob", "ops"), Times.Once);
    }

    [TestMethod]
    public async Task AnUnchangedOwnerAsksForNothing()
    {
        var rig = await OpenAsync(Info());

        await rig.Dialog.HandleKeyEventAsync(Char('o'));
        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        rig.Service.Verify(s => s.SetOwner(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
        StringAssert.Contains(rig.Dialog.StatusForTest, "unchanged");
    }
}
