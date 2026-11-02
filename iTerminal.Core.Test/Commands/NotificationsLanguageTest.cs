using System.Text.RegularExpressions;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The messages the git, Defender, secure wipe, format, eject and clipboard actions raise follow the chosen language.</summary>
[TestClass]
public class NotificationsLanguageTest
{
    private static readonly string[] Keys = ["Notify.PullingRebase","Notify.NotGitRepo","Notify.PulledRebase","Notify.PullRebaseFailed","Notify.GitSyncing","Notify.GitSynced","Notify.GitSyncNoUpstream","Notify.GitSyncPullFailed","Notify.GitSyncRebaseNote","Notify.GitSyncPushFailed","Notify.GitSyncFailed","Notify.Scanning","Notify.ThreatsFound","Notify.DefenderFailed","Notify.NoThreats","Notify.Eject","Notify.Format","Notify.Clipboard","Notify.Paste","Notify.WipeStopped","Notify.WipeStoppedBody","Notify.WipeErrors","Notify.WipeErrorsBody","Notify.WipeFinished","Notify.WipeFinishedBody","Notify.ClipboardNoFiles","Notify.SkippedCount"];

    private IDisposable? _language;

    [TestCleanup]
    public void Reset() => _language?.Dispose();

    private Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(SortDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        _language?.Dispose();
        _language = UiText.Scope(localizer);
        return localizer;
    }

    private static string[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().Order()];

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryMessage_AndKeepsItsPlaceholders()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                Assert.IsTrue(localizer.TryGet(key, out string text), language.Code + " " + key);
                Assert.IsTrue(english.TryGet(key, out string englishText), key);
                Assert.AreNotEqual(englishText, text, language.Code + " " + key);
                CollectionAssert.AreEquivalent(Placeholders(englishText), Placeholders(text), language.Code + " " + key);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFormattedMessages_FillTheirPlaceholders_InEveryLanguage()
    {
        _language?.Dispose();
        _language = UiText.Scope(null);
        Assert.AreEqual("3 skipped: a.txt", string.Format(UiText.Get("Notify.SkippedCount", "{0} skipped: {1}"), 3, "a.txt"));

        Speak("de");
        Assert.AreEqual("3 übersprungen: a.txt", string.Format(UiText.Get("Notify.SkippedCount", "{0} skipped: {1}"), 3, "a.txt"));
    }
}
