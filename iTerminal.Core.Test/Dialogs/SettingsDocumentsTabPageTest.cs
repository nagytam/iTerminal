using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Documents;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Tab 5 of the settings dialog: the documents folder and the OCR switch.</summary>
[TestClass]
public class SettingsDocumentsTabPageTest
{
    private static SettingsDocumentsTabPage CreatePage(ISettingsService settingsService) =>
        new(settingsService, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());

    [TestMethod]
    public async Task OnInitialize_LoadsTheStoredFolderAndOcrSwitch()
    {
        var settings = new SettingsService();
        settings.Set(Settings.DocumentsFolder, @"D:\Papers");
        settings.Set(Settings.DocumentsOcrEnabled, bool.TrueString);

        var page = CreatePage(settings);
        await page.OnInitializeAsync();

        Assert.AreEqual(@"D:\Papers", page.DocumentsFolder);
        Assert.IsTrue(page.IsOcrEnabled);
    }

    [TestMethod]
    public async Task EditingTheFolder_WritesTheSettingStraightAway()
    {
        var settings = new SettingsService();
        var page = CreatePage(settings);
        await page.OnInitializeAsync();

        page.DocumentsFolder = @"C:\Docs";

        Assert.AreEqual(@"C:\Docs", settings.Get(Settings.DocumentsFolder));
    }

    [TestMethod]
    public async Task AnEmptyBoxSpellsOutTheDefaultFolder()
    {
        var settings = new SettingsService();
        var page = CreatePage(settings);
        await page.OnInitializeAsync();

        StringAssert.StartsWith(page.HintForTest, "Empty uses the default folder: ");
        StringAssert.Contains(page.HintForTest, DocumentLibraryService.ResolveDocumentsFolder(settings));
    }

    [TestMethod]
    public async Task AnExistingFolderIsConfirmed()
    {
        var settings = new SettingsService();
        var page = CreatePage(settings);
        await page.OnInitializeAsync();

        page.DocumentsFolder = Path.GetTempPath();

        StringAssert.StartsWith(page.HintForTest, "Folder found");
    }

    [TestMethod]
    public async Task AMissingFolderIsFlagged()
    {
        var settings = new SettingsService();
        var page = CreatePage(settings);
        await page.OnInitializeAsync();

        page.DocumentsFolder = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"));

        Assert.AreEqual("Folder does not exist yet.", page.HintForTest);
    }

    [TestMethod]
    public async Task TurningOcrOnIsStoredAndExplained()
    {
        var settings = new SettingsService();
        var page = CreatePage(settings);
        await page.OnInitializeAsync();
        StringAssert.Contains(page.OcrHintForTest, "embedded captions only");

        page.IsOcrEnabled = true;

        Assert.AreEqual(bool.TrueString, settings.Get(Settings.DocumentsOcrEnabled));
        StringAssert.Contains(page.OcrHintForTest, "Tesseract");
    }

    [TestMethod]
    public void TheHotKeyIsFive()
    {
        Assert.AreEqual(ConsoleKey.D5, CreatePage(new SettingsService()).HotConsoleKey);
    }

    [TestMethod]
    public async Task LayoutStacksCaptionBoxHintAndOcrRows()
    {
        var page = CreatePage(new SettingsService());
        page.ViewSize = new Size(60, 10);
        await page.OnInitializeAsync();

        page.OnInitializeLayout();

        var rows = page.Controls.AsSnapshot().Select(c => (int)c.Position.Y).ToList();
        CollectionAssert.AreEqual(new[] { 0, 1, 3, 5, 6 }, rows);
        Assert.IsTrue(page.Controls.AsSnapshot().All(c => c.ViewSize.Width == 60));
    }
}
