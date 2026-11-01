using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Services.UserMenu;
using iTerminal.Core.Services.Updates;
using iTerminal.Core.Services.Wipe;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The help overlay (F1) lists the keys of the focused dialog; these dialogs took keys it never showed.</summary>
[TestClass]
public class DialogKeyHintsTest
{
    private static string[] Keys(IEnumerable<(string Key, string Description)> hints) => [.. hints.Select(h => h.Key)];

    [Timeout(60_000)]
    [TestMethod]
    public void UserMenu_KeyHints_NameEnterTheEntryKeysAndF4()
    {
        UserMenuDialog dialog = new(Mock.Of<IUserMenuService>(), Mock.Of<IFileLauncher>(), TestTheme(), Mock.Of<INotificationService>());

        CollectionAssert.IsSubsetOf(new[] { "Up/Down", "Enter", "Hotkey", "F4" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Update_KeyHints_NameInstallAndSkip()
    {
        UpdateDialog dialog = new(Mock.Of<IUpdateService>(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Enter", "S" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StripMetadata_KeyHints_FollowThePhase()
    {
        Mock<IContentModelService> panels = new();
        panels.SetupGet(p => p.ContentModels).Returns([]);
        StripMetadataDialog dialog = new(panels.Object, TestTheme());

        CollectionAssert.AreEqual(new[] { "Enter" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SecureWipe_KeyHints_FollowThePhase()
    {
        Mock<IContentModelService> panels = new();
        panels.SetupGet(p => p.ContentModels).Returns([]);
        SecureWipeDialog dialog = new(Mock.Of<ISecureWipeService>(), new NotificationService(), panels.Object, TestTheme());
        dialog.Load([@"C:\data\secret.txt"]);
        await ShowAsync(dialog);

        CollectionAssert.IsSubsetOf(new[] { "1/3/7", "Enter" }, Keys(dialog.GetKeyHints()));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(WipePhase.Armed, dialog.Phase);
        CollectionAssert.AreEqual(new[] { "Enter", "Any other key" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StartupWarning_KeyHints_NameEnter()
    {
        StartupWarningDialog dialog = new(["Disk almost full"], TestTheme());

        CollectionAssert.AreEqual(new[] { "Enter" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Checksum_KeyHints_NameTheSaveAndCopyKeys()
    {
        ChecksumDialog dialog = new(Mock.Of<IChecksumService>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "F2", "F3", "Enter" }, Keys(dialog.GetKeyHints()));
    }
}
