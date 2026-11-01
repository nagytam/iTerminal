using System.Text.RegularExpressions;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The empty-list texts, placeholders, sync options, update, sign-in and indexing notices of the dialogs and services follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class EmptyListsAndNoticesLanguageTest
{
    private static readonly string[] Keys = ["DiskUsage.Partial","DiskUsage.ByName","DiskUsage.BySize","Placeholder.DocumentWords","Msg.LinkPath","Label.Command","Placeholder.CommandExample","Label.Name","Msg.LabelsMultiple","Dialog.LabelsTitleFile","Dialog.LabelsTitle","Empty.Favourites","Empty.History","Placeholder.PathExample","Empty.GitStaged","Empty.GitBranches","Empty.GitCommits","Empty.GitStashes","Empty.IPhoneLooking","Empty.HostsLooking","Empty.HostsNone","Empty.Loading","Empty.PackagesFailed","Empty.Processes","Empty.Screens","Empty.FindFiles","Empty.Watchlist","Empty.StockNothing","Empty.Sync","Sync.BothWays","Sync.LeftToRight","Sync.RightToLeft","Sync.Options","Sync.Yes","Sync.No","Msg.ComparingProgress","Empty.Tasks","Empty.Themes","Notify.UpdateAvailable","Notify.UpdateDetail","Web.Stopped","Web.Loading","OAuth.SignIn","OAuth.SignInDetail","OAuth.SignedIn","OAuth.SignedInDetail","OAuth.Failed","OAuth.Refused","OAuth.Expired","OAuth.ExpiredDetail","Msg.DocIndexFailed","Msg.IndexedTrack","Msg.IndexedTracks","Msg.MusicIndexFailed"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(SortDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    /// <summary>Short words that are spelled the same in some languages.</summary>
    private static bool SameWordInThatLanguage(string code, string key) =>
        (key == "Label.Name" && code == "de") ||
        (key == "Sync.No" && code is "es" or "it");

    private static string[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().Order()];

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryTitle_AndKeepsItsPlaceholders()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                Assert.IsTrue(localizer.TryGet(key, out string text), language.Code + " " + key);
                Assert.IsTrue(english.TryGet(key, out string englishText), key);
                if (!SameWordInThatLanguage(language.Code, key))
                {
                    Assert.AreNotEqual(englishText, text, language.Code + " " + key);
                }

                CollectionAssert.AreEquivalent(Placeholders(englishText), Placeholders(text), language.Code + " " + key);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSyncOptions_FillTheirPlaceholders_InEveryLanguage()
    {
        UiText.Current = null;
        Assert.AreEqual(" Direction: left to right   Subfolders: yes   Delete extras: no", string.Format(UiText.Get("Sync.Options", " Direction: {0}   Subfolders: {1}   Delete extras: {2}"), "left to right", "yes", "no"));

        Speak("de");
        Assert.AreEqual(" Richtung: x   Unterordner: ja   Überzählige löschen: nein", string.Format(UiText.Get("Sync.Options", "?"), "x", "ja", "nein"));
    }
}
