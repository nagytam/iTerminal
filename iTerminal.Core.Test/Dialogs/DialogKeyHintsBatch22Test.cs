using System.Resources;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.Calculator;
using iTerminal.Core.Dialogs.Calendar;
using iTerminal.Core.Dialogs.Map;
using iTerminal.Core.Dialogs.Music;
using iTerminal.Core.Dialogs.Stocks;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Model.Calendar;
using iTerminal.Core.Services;
using iTerminal.Core.Services.AccessControl;
using iTerminal.Core.Services.Map;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Services.Signing;
using iTerminal.Core.Services.Stocks;
using iTerminal.Core.Video;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The help overlay (F1) lists the keys of the focused dialog; these dialogs took keys it never showed.</summary>
[TestClass]
public class DialogKeyHintsBatch22Test
{
    private static string[] Keys(IEnumerable<(string Key, string Description)> hints) => [.. hints.Select(h => h.Key)];

    [Timeout(60_000)]
    [TestMethod]
    public void Language_KeyHints_NameChooseAndApply()
    {
        Localizer localizer = new(
            [new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(LanguageDialog).Assembly))],
            new SettingsLanguageStore(new SettingsService()));
        LanguageDialog dialog = new(localizer, TestTheme());

        CollectionAssert.AreEqual(new[] { "Up/Down", "Home/End", "Enter" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Signature_KeyHints_NameTheSigningAndVerifyKeys()
    {
        SignatureDialog dialog = new(Mock.Of<ISignatureService>(), Mock.Of<INotificationService>(), Mock.Of<IContentModelService>(),
            Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "F2", "F3", "F4", "F5" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Calendar_KeyHints_NameMovingAddingAndDeleting()
    {
        CalendarDialog dialog = new(new DateTimeProviderService(), new MeetingStore(Mock.Of<ISettingsService>()), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "PageUp/PageDown", "Home", "Enter/Insert/N", "F2", "Delete" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MusicFileViewer_KeyHints_NamePlaySeekVolumeAndTags()
    {
        MusicFileViewerDialog dialog = new(Mock.Of<IMusicPlayerService>(), Mock.Of<IAudioDeviceService>(), Mock.Of<IMusicMetadataService>(),
            Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Space", "Left/Right", "+/-", "S", "E" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VideoFileViewer_KeyHints_NamePlaySeekStopAndColour()
    {
        VideoFileViewerDialog dialog = new(Mock.Of<IVideoDecoderFactory>(), TestTheme());

        CollectionAssert.AreEqual(new[] { "Space", "Left/Right", "Home", "S", "C" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Calculator_KeyHints_NameEvaluateAndClear()
    {
        CalculatorDialog dialog = new(TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Enter/=", "Backspace", "Delete/C" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Map_KeyHints_NamePanZoomAndSearch()
    {
        MapDialog dialog = new(Mock.Of<IMapTileService>(), Mock.Of<IMapSearchService>(), new MapStore(Mock.Of<ISettingsService>()), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Arrows", "+/-", "/ or G", "F1" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StockBrowser_KeyHints_NameAddRemoveAndRefresh()
    {
        StockBrowserDialog dialog = new(Mock.Of<IStockService>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.AreEqual(new[] { "A", "D/Delete", "R" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ImageFileViewer_KeyHints_OfferTheScrollKeysOnlyWhileTheInformationShows()
    {
        ImageFileViewerDialog dialog = new(Mock.Of<IVideoDecoderFactory>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.AreEqual(new[] { "C", "I" }, Keys(dialog.GetKeyHints()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AccessControl_KeyHints_NameTheListKeys()
    {
        AccessControlDialog dialog = new(Mock.Of<IAccessControlService>(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Insert/A", "Enter/E", "Delete/D", "I", "O", "R" }, Keys(dialog.GetKeyHints()));
    }
}
