using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.Mail;
using iTerminal.Core.Dialogs.Music;
using iTerminal.Core.Dialogs.Remote;
using iTerminal.Core.Dialogs.UnitConverter;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Dialogs.Web;
using iTerminal.Core.Model.Mail;
using iTerminal.Core.Model.Web;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Images;
using iTerminal.Core.Services.Mail;
using iTerminal.Core.Services.Remote;
using iTerminal.Core.Services.Shell;
using iTerminal.Core.Services.Web;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The help overlay (F1) lists the keys of the focused dialog; these dialogs took keys it never showed.</summary>
[TestClass]
public class DialogKeyHintsBatch24Test
{
    private static string[] Keys(IEnumerable<(string Key, string Description)> hints) => [.. hints.Select(h => h.Key)];

    [TestMethod]
    public void RemoteConnections_KeyHints_NameTheListKeys()
    {
        RemoteConnectionManagerDialog dialog = new(Mock.Of<IRemoteConnectionService>(), Mock.Of<IInteractiveShellService>(),
            Mock.Of<IContentModelService>(), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Enter", "N", "E", "D/Delete", "B" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public void Mail_KeyHints_ListTheInboxKeys()
    {
        Mock<ISettingsService> settings = new();
        MailAccountStore accounts = new(settings.Object);
        accounts.Save(new MailAccount { Name = "Work", PopHost = "pop.example.test", SmtpHost = "smtp.example.test", UserName = "ada", FromAddress = "ada@example.test" });
        MailDialog dialog = new(accounts, Mock.Of<IMailService>(), Mock.Of<IPasswordManager>(), TestTheme());

        Assert.IsTrue(dialog.GetKeyHints().Any());
    }

    [TestMethod]
    public void WebBrowser_KeyHints_NameAddressLinksTabsAndFind()
    {
        WebBrowserDialog dialog = new(Mock.Of<IWebFetcher>(), new WebBrowserStore(Mock.Of<ISettingsService>()), TestTheme());

        CollectionAssert.IsSubsetOf(new[] { "Ctrl+L / F6", "Tab", "Enter", "Ctrl+T / Ctrl+W", "F1" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public void MusicTagEditor_KeyHints_NameSaveAndClose()
    {
        MusicTagEditorDialog dialog = new("song.mp3", new MusicTagSet(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.AreEqual(new[] { "Tab", "Enter", "Esc" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public void ImageMetadataEditor_KeyHints_NameSaveAndClose()
    {
        ImageMetadataEditorDialog dialog = new("photo.png", new ImageMetadataEdit("", "", "", ""), Mock.Of<IBeepService>(),
            new DateTimeProviderService(), TestTheme());

        CollectionAssert.AreEqual(new[] { "Tab", "Enter", "Esc" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public void NetworkBrowser_KeyHints_NameOpenRescanAndDelete()
    {
        NetworkBrowserDialog dialog = new(Mock.Of<INetworkDiscoveryService>(), Mock.Of<INetworkProfileService>(), Mock.Of<IPasswordManager>(),
            Mock.Of<IContentModelService>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.AreEqual(new[] { "Enter", "R", "D", "Esc" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public void UnitConverter_KeyHints_NameTabAndEsc()
    {
        UnitConverterDialog dialog = new(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        CollectionAssert.AreEqual(new[] { "Tab", "Esc" }, Keys(dialog.GetKeyHints()));
    }
}
