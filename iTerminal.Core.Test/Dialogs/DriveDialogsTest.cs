using iConsole.Controls;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Drives;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Mount a disk image and format a drive: what each asks for, what it refuses, and how an error leaves when the user types on.</summary>
[TestClass]
public class DriveDialogsTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _folder = Path.Combine(Path.GetTempPath(), "drive-dialogs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch
        {
        }
    }

    private static IContentModelService Panels(out Mock<IContentModel> panel)
    {
        panel = new Mock<IContentModel>();
        panel.SetupGet(m => m.Url).Returns(new Url(Path.GetTempPath()));
        Mock<IContentModelService> service = new();
        ConcurrentList<IContentModel> list = [];
        list.Add(panel.Object);
        service.SetupGet(s => s.ContentModels).Returns(list);
        return service.Object;
    }

    // ---- Disk image ----

    private sealed class ImageRig
    {
        public required DiskImageDialog Dialog { get; init; }

        public required Control Root { get; init; }

        public required Mock<IDriveActionsService> Drives { get; init; }

        public required Mock<INotificationService> Notifications { get; init; }

        public required Mock<IContentModel> Panel { get; init; }
    }

    private async Task<ImageRig> OpenImageAsync(string path, DriveActionResult? result = null)
    {
        Mock<IDriveActionsService> drives = new();
        drives.Setup(d => d.MountImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(result ?? new DriveActionResult(true, "Mounted as E:"));
        drives.Setup(d => d.UnmountImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(result ?? new DriveActionResult(true, "Unmounted"));
        Mock<INotificationService> notifications = new();
        DiskImageDialog dialog = new(drives.Object, notifications.Object, Panels(out Mock<IContentModel> panel), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load(path);
        Control root = await ShowAsync(dialog);
        return new ImageRig { Dialog = dialog, Root = root, Drives = drives, Notifications = notifications, Panel = panel };
    }

    private string MakeImage(string name = "disc.iso")
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Image_ShowsThePathAndWhatIsAccepted()
    {
        ImageRig rig = await OpenImageAsync(MakeImage());

        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Disk image");
        AssertScreenContains(screen, "disc.iso");
        AssertScreenContains(screen, "An .iso, .vhd or .vhdx file.");
        AssertScreenContains(screen, "Mount");
        AssertScreenContains(screen, "Unmount");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Image_Mount_RunsTheAction_Notifies_ClosesAndRereadsLocalPanels()
    {
        string path = MakeImage();
        ImageRig rig = await OpenImageAsync(path);

        Assert.IsTrue(await rig.Dialog.RunAsync(mount: true));

        rig.Drives.Verify(d => d.MountImageAsync(path, It.IsAny<CancellationToken>()), Times.Once);
        rig.Notifications.Verify(n => n.Notify("Mounted", "Mounted as E:", NotificationSeverity.Success), Times.Once);
        Assert.IsFalse(rig.Dialog.IsVisible);
        rig.Panel.Verify(m => m.Refresh(true), Times.Once);
        Assert.AreEqual("Mounted as E:", rig.Dialog.LastMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Image_Unmount_DoesNotNeedTheFileToExist()
    {
        string path = Path.Combine(_folder, "gone.vhd");
        ImageRig rig = await OpenImageAsync("\"" + path + "\"");

        Assert.IsTrue(await rig.Dialog.RunAsync(mount: false));

        rig.Drives.Verify(d => d.UnmountImageAsync(path, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Image_RefusesAnEmptyPath_ATypeItCannotMount_AndAMissingFile()
    {
        ImageRig rig = await OpenImageAsync(string.Empty);
        Assert.IsFalse(await rig.Dialog.RunAsync(true));
        StringAssert.Contains(rig.Dialog.Error, "Type the path");

        rig.Dialog.ImagePath = Path.Combine(_folder, "notes.txt");
        Assert.IsFalse(await rig.Dialog.RunAsync(true));
        StringAssert.Contains(rig.Dialog.Error, "Only .iso, .vhd and .vhdx");

        rig.Dialog.ImagePath = Path.Combine(_folder, "missing.iso");
        Assert.IsFalse(await rig.Dialog.RunAsync(true));
        StringAssert.Contains(rig.Dialog.Error, "does not exist");

        rig.Drives.Verify(d => d.MountImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(rig.Dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Image_AFailureStaysInTheDialog_Wrapped()
    {
        ImageRig rig = await OpenImageAsync(MakeImage(), new DriveActionResult(false, "The image is already attached; unmount it first, then try again from a new window."));

        Assert.IsFalse(await rig.Dialog.RunAsync(true));

        Assert.IsTrue(rig.Dialog.IsVisible);
        StringAssert.Contains(rig.Dialog.Error, "already attached");
        AssertScreenContains(Render(rig.Root), "Error: The image is already attached");
        rig.Notifications.Verify(n => n.Notify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NotificationSeverity>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Image_TheErrorLeavesWhenTheUserTypesOn_ButNotOnTab()
    {
        ImageRig rig = await OpenImageAsync(string.Empty);
        await rig.Dialog.RunAsync(true);
        Assert.AreNotEqual(string.Empty, rig.Dialog.Error);

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        Assert.AreNotEqual(string.Empty, rig.Dialog.Error);

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreNotEqual(string.Empty, rig.Dialog.Error);

        await rig.Dialog.HandleKeyEventAsync(Char('d'));
        Assert.AreEqual(string.Empty, rig.Dialog.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Image_Escape_Closes()
    {
        ImageRig rig = await OpenImageAsync(MakeImage());

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(rig.Dialog.IsVisible);
        rig.Drives.Verify(d => d.MountImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Format drive ----

    private sealed class FormatRig
    {
        public required FormatDriveDialog Dialog { get; init; }

        public required Control Root { get; init; }

        public required Mock<IDriveActionsService> Drives { get; init; }

        public required Mock<IContentModel> Panel { get; init; }
    }

    private static async Task<FormatRig> OpenFormatAsync(DriveActionResult? result = null, string fileSystem = "ReFS")
    {
        Mock<IDriveActionsService> drives = new();
        drives.Setup(d => d.FormatAsync(It.IsAny<string>(), It.IsAny<DriveFormatOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(result ?? new DriveActionResult(true, "Formatted E:"));
        FormatDriveDialog dialog = new(drives.Object, Mock.Of<INotificationService>(), Panels(out Mock<IContentModel> panel), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load("E:\\", "BACKUP", fileSystem);
        Control root = await ShowAsync(dialog);
        return new FormatRig { Dialog = dialog, Root = root, Drives = drives, Panel = panel };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Format_NamesTheDrive_AndWarnsEverythingWillBeErased()
    {
        FormatRig rig = await OpenFormatAsync();

        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Format drive");
        AssertScreenContains(screen, "Drive E:");
        AssertScreenContains(screen, "Everything on this drive will be erased.");
        AssertScreenContains(screen, "BACKUP");
        AssertScreenContains(screen, "Quick format");
        Assert.IsTrue(rig.Dialog.Quick);
        Assert.AreEqual("ReFS", rig.Dialog.FileSystem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Format_AnUnknownCurrentFileSystem_FallsBackToNtfs()
    {
        FormatRig rig = await OpenFormatAsync(fileSystem: "ext4");

        Assert.AreEqual("NTFS", rig.Dialog.FileSystem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Format_WillNotRunUntilTheDriveLetterIsTyped()
    {
        FormatRig rig = await OpenFormatAsync();

        Assert.IsFalse(await rig.Dialog.FormatAsync());
        StringAssert.Contains(rig.Dialog.Error, "Type E to confirm that E: is to be erased.");

        rig.Dialog.Confirmation = "F";
        Assert.IsFalse(await rig.Dialog.FormatAsync());

        rig.Drives.Verify(d => d.FormatAsync(It.IsAny<string>(), It.IsAny<DriveFormatOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(rig.Dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Format_WithTheLetterTyped_FormatsWithTheChosenOptions_ClosesAndRereadsPanels()
    {
        FormatRig rig = await OpenFormatAsync();
        rig.Dialog.Confirmation = "e:";
        rig.Dialog.VolumeLabel = " DATA ";
        rig.Dialog.FileSystem = "exFAT";
        rig.Dialog.Quick = false;

        Assert.IsTrue(await rig.Dialog.FormatAsync());

        rig.Drives.Verify(d => d.FormatAsync("E:\\", new DriveFormatOptions("exFAT", "DATA", false), It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsFalse(rig.Dialog.IsVisible);
        rig.Panel.Verify(m => m.Refresh(true), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Format_AFailureStaysInTheDialog_AndTheWarningComesBack()
    {
        FormatRig rig = await OpenFormatAsync(new DriveActionResult(false, "The volume is in use by another process."));
        rig.Dialog.Confirmation = "E";

        Assert.IsFalse(await rig.Dialog.FormatAsync());

        Assert.IsTrue(rig.Dialog.IsVisible);
        StringAssert.Contains(rig.Dialog.Error, "in use by another process");
        AssertScreenContains(Render(rig.Root), "Everything on this drive will be erased.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Format_ARootThatIsNotALetteredDrive_IsRefused()
    {
        FormatRig rig = await OpenFormatAsync();
        rig.Dialog.Load("/media/usb", "USB", "NTFS");

        Assert.IsFalse(await rig.Dialog.FormatAsync());

        Assert.AreEqual("Error: Not a lettered drive.", rig.Dialog.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Format_TheErrorLeavesWhenTheUserTypesTheLetter()
    {
        FormatRig rig = await OpenFormatAsync();
        await rig.Dialog.FormatAsync();
        Assert.AreNotEqual(string.Empty, rig.Dialog.Error);

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));

        Assert.AreEqual(string.Empty, rig.Dialog.Error);
    }

    // ---- Error clearing in the other forms ----

    [Timeout(60_000)]
    [TestMethod]
    public void EditingKeys_AreCharactersBackspaceAndDelete_NotMovesOrChords()
    {
        Assert.IsTrue(FormDialogProbe.IsEditing(Char('a')));
        Assert.IsTrue(FormDialogProbe.IsEditing(Key(ConsoleKey.Spacebar, ' ')));
        Assert.IsTrue(FormDialogProbe.IsEditing(Key(ConsoleKey.Backspace)));
        Assert.IsTrue(FormDialogProbe.IsEditing(Key(ConsoleKey.Delete)));
        Assert.IsFalse(FormDialogProbe.IsEditing(Key(ConsoleKey.Tab, '\t')));
        Assert.IsFalse(FormDialogProbe.IsEditing(Key(ConsoleKey.Enter, '\r')));
        Assert.IsFalse(FormDialogProbe.IsEditing(Key(ConsoleKey.Escape, '\u001b')));
        Assert.IsFalse(FormDialogProbe.IsEditing(Key(ConsoleKey.LeftArrow)));
        Assert.IsFalse(FormDialogProbe.IsEditing(Key(ConsoleKey.A, 'a', control: true)));
        Assert.IsFalse(FormDialogProbe.IsEditing(Key(ConsoleKey.F, 'f', alt: true)));
    }

    private sealed class FormDialogProbe(IThemeService theme) : FormDialog("probe", theme)
    {
        public static bool IsEditing(KeyEvent keyEvent) => IsEditingKey(keyEvent);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreateLink_TheErrorLeavesWhenTheUserTypes()
    {
        Mock<iTerminal.Core.Services.Links.ILinkService> links = new();
        CreateLinkDialog dialog = new(links.Object, Panels(out _), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load(Path.Combine(_folder, "a.txt"), false, string.Empty);
        await ShowAsync(dialog);
        await dialog.CreateAsync();
        StringAssert.Contains(dialog.Error, "Type where the link goes.");

        await dialog.HandleKeyEventAsync(Char('x'));

        Assert.AreEqual(string.Empty, dialog.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Properties_TheErrorLeavesWhenTheUserTypes()
    {
        Mock<iTerminal.Core.Services.Properties.IFilePropertiesService> service = new();
        service.Setup(s => s.Read(It.IsAny<string>())).Throws(new IOException("gone"));
        PropertiesDialog dialog = new(service.Object, Panels(out _), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load("x");
        await ShowAsync(dialog);
        Assert.AreEqual("Error: gone", dialog.Error);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        Assert.AreEqual("Error: gone", dialog.Error);

        await dialog.HandleKeyEventAsync(Char('7'));
        Assert.AreEqual(string.Empty, dialog.Error);
    }
}
