using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.ToDo;
using iTerminal.Core.Dialogs.Workspaces;
using iTerminal.Core.Model.ToDo;
using iTerminal.Core.Services;
using iTerminal.Core.Services.DiskUsage;
using iTerminal.Core.Services.Duplicates;
using iTerminal.Core.Services.RuntimeDependencies;
using iTerminal.Core.Services.Workspaces;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The help overlay (F1) lists the keys of the focused dialog; these dialogs took keys it never showed.</summary>
[TestClass]
public class DialogKeyHintsBatch21Test
{
    private static string[] Keys(IEnumerable<(string Key, string Description)> hints) => [.. hints.Select(h => h.Key)];

    private static IContentModelService Panels()
    {
        Mock<IContentModelService> service = new();
        service.SetupGet(s => s.ContentModels).Returns([]);
        return service.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RuntimeDependencies_WithoutAWizard_NameOnlyEnter()
    {
        RuntimeDependencyDialog dialog = new(Mock.Of<IRuntimeDependencyService>(), TestTheme());

        CollectionAssert.AreEqual(new[] { "Enter" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DiskUsage_KeyHints_NameNavigationSortRescanAndGoTo()
    {
        DiskUsageDialog dialog = new(Mock.Of<IDiskUsageService>(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Up/Down", "Enter/Right", "S", "R", "G" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeManager_KeyHints_NameChooseAndApply()
    {
        ThemeManagerDialog dialog = new(TestTheme());

        CollectionAssert.AreEqual(new[] { "Up/Down", "Enter/Space" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompareFolders_KeyHints_NameCompareAndSynchronise()
    {
        IContentModelService panels = Panels();
        CompareFoldersDialog dialog = new(panels, new FileSystemResolver(null!, null!, null!, null!, panels), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Enter", "F5" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SynchronizeFolders_KeyHints_NameEnterAndEsc_AndTheEnterMeansPreviewFirst()
    {
        IContentModelService panels = Panels();
        SynchronizeFoldersDialog dialog = new(panels, new FileSystemResolver(null!, null!, null!, null!, panels), TestTheme());

        (string Key, string Description)[] hints = [.. dialog.GetKeyHints()];

        CollectionAssert.AreEqual(new[] { "Enter", "Esc" }, Keys(hints));
        StringAssert.Contains(hints[0].Description, "Preview");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DuplicateFinder_KeyHints_NameTheMarkingAndDeleteKeys()
    {
        DuplicateFinderDialog dialog = new(Mock.Of<IDuplicateFinderService>(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Space/Insert", "A", "N", "U", "Delete/F8" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToDoList_KeyHints_NameTheListKeys()
    {
        ToDoListDialog dialog = new(new DateTimeProviderService(), new ToDoStore(Mock.Of<ISettingsService>()), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Enter/Insert", "F2", "Space", "Delete" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileDiff_KeyHints_NameNextPreviousAndTheHexSwitch()
    {
        FileDiffDialog dialog = new(TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "N/P", "F4", "W", "C" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommandPalette_KeyHints_NameFilterChooseAndRun()
    {
        CommandPaletteDialog dialog = new(Mock.Of<ISettingsService>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Type", "Up/Down", "Enter" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WorkspaceManager_KeyHints_NameSaveSwitchRenameAndDelete()
    {
        WorkspaceManagerDialog dialog = new(Mock.Of<IWorkspaceService>(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Enter", "1-9", "S/N", "R", "D/Delete" }, Keys(dialog.GetKeyHints()));
    }
}
