using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.CurrencyConverter;
using iTerminal.Core.Dialogs.Documents;
using iTerminal.Core.Dialogs.Dotnet;
using iTerminal.Core.Dialogs.PackageManager;
using iTerminal.Core.Dialogs.PasswordManager;
using iTerminal.Core.Dialogs.Search;
using iTerminal.Core.Dialogs.Translator;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Currency;
using iTerminal.Core.Services.Documents;
using iTerminal.Core.Services.Dotnet;
using iTerminal.Core.Services.Drives;
using iTerminal.Core.Services.PackageManager;
using iTerminal.Core.Services.Search;
using iTerminal.Core.Services.Translation;
using Microsoft.Extensions.Options;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The help overlay (F1) lists the keys of the focused dialog; these dialogs took keys it never showed.</summary>
[TestClass]
public class DialogKeyHintsBatch23Test
{
    private static string[] Keys(IEnumerable<(string Key, string Description)> hints) => [.. hints.Select(h => h.Key)];

    [Timeout(60_000)]
    [TestMethod]
    public void ProcessManager_KeyHints_NameFilterAndMovement()
    {
        ProcessManagerDialog dialog = new(Mock.Of<IBeepService>(), new ProcessService(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Type", "Up/Down", "Ctrl+Home/End" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DiskManager_KeyHints_AreOfferedOnlyWhenDriveActionsExist()
    {
        using TaskManager taskManager = new();
        DiskManagerDialog without = new(taskManager, new DriveInfoModel(), TestTheme());
        DiskManagerDialog with = new(taskManager, new DriveInfoModel(), TestTheme(), Mock.Of<IDriveActionsService>());

        Assert.AreEqual(0, without.GetKeyHints().Count());
        CollectionAssert.AreEqual(new[] { "E", "F", "M", "B" }, Keys(with.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SolutionViewer_KeyHints_NameBuildCleanTestAndRun()
    {
        SolutionViewerDialog dialog = new(Mock.Of<IDotnetSolutionService>(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Space", "A", "Shift+T", "B", "C", "T", "R", "X", "V" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DocumentManager_KeyHints_NameSearchRescanAndPreview()
    {
        DocumentManagerDialog dialog = new(Mock.Of<IDocumentLibraryService>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Type", "Enter", "Ctrl+R", "Ctrl+X", "Ctrl+T" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CurrencyConverter_KeyHints_NameRefreshAndSwap()
    {
        CurrencyConverterDialog dialog = new(Mock.Of<ICurrencyService>(), Mock.Of<ISettingsService>(), Mock.Of<IBeepService>(),
            new DateTimeProviderService(), TestTheme());

        CollectionAssert.AreEqual(new[] { "F5", "F6" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IPhoneBrowser_KeyHints_NameConnectAndRescan()
    {
        IPhoneBrowserDialog dialog = new(Mock.Of<IIPhoneDeviceService>(), Mock.Of<IContentModelService>(), TestTheme());

        CollectionAssert.AreEqual(new[] { "Enter", "R", "Esc" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FindFiles_KeyHints_NameSearchSendAndCancel()
    {
        FindFilesDialog dialog = new(Mock.Of<IFileSearchService>(), Mock.Of<IContentModelService>(), Mock.Of<IBeepService>(),
            new DateTimeProviderService(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Enter", "Ctrl+P", "Ctrl+X" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PasswordManager_KeyHints_NameTheListKeys()
    {
        PasswordManagerDialog dialog = new(Mock.Of<IPasswordManager>(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "N", "Enter/E", "Delete/D", "V" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PackageManager_KeyHints_NameRefreshSearchAndInstalled()
    {
        PackageManagerDialog dialog = new(Mock.Of<IPackageManagerService>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "R", "S", "I", "X" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Translator_KeyHints_NameTranslateSwapDetectAndClear()
    {
        TranslatorDialog dialog = new(Mock.Of<ITranslationService>(), Mock.Of<ISettingsService>(), Options.Create(new TranslationOptions()), TestTheme());

        CollectionAssert.AreEqual(new[] { "F5", "F6", "F7", "F8" }, Keys(dialog.GetKeyHints()));
    }
}
