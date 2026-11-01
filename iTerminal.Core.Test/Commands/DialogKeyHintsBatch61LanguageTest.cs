using iConsole.Dialogs;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The key hints of the file diff, disk usage, package manager, network browser, document manager, stock browser and Git client dialogs follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogKeyHintsBatch61LanguageTest
{
    private static readonly string[] Keys = ["DialogHint.ScrollDifferences","DialogHint.ScrollPageAtATime","DialogHint.JumpStartOrEnd","DialogHint.JumpNextPrevDifference","DialogHint.IgnoreWhiteSpace","DialogHint.IgnoreLetterCase","DialogHint.MoveOverFolders","DialogHint.JumpFirstLastRow","DialogHint.GoIntoFolder","DialogHint.SortNameOrSize","DialogHint.ShowFolderInPanel","DialogHint.Submit","DialogHint.RunMainAction","DialogHint.RefreshList","DialogHint.SearchPackages","DialogHint.ShowInstalled","DialogHint.DeleteSavedProfile","DialogHint.KeepProfile","DialogHint.Connect","DialogHint.BackToHostList","DialogHint.OpenChosenHost","DialogHint.SearchNetworkAgain","DialogHint.DeleteSavedProfileAsks","DialogHint.SearchDocuments","DialogHint.ChooseResult","DialogHint.ShowDocumentInPanel","DialogHint.ClearSearchThenClose","DialogHint.ScanDocumentsAgain","DialogHint.CancelScan","DialogHint.ShowHidePreview","DialogHint.SearchOrAddHit","DialogHint.ChooseHit","DialogHint.AddStock","DialogHint.RemoveStock","DialogHint.RefreshQuotes","DialogHint.ScrollHelp","DialogHint.BackToRepository","DialogHint.UseTheText","DialogHint.NextSection","DialogHint.StageCheckoutShow","DialogHint.StageUnstageFile","DialogHint.StageAll","DialogHint.DiscardFileChangesAsks","DialogHint.Commit","DialogHint.AmendCommit","DialogHint.NewBranch","DialogHint.MergeBranch","DialogHint.DeleteBranch","DialogHint.Fetch","DialogHint.PullPush","DialogHint.StashChanges","DialogHint.DropStash","DialogHint.Refresh","DialogHint.ScrollPatch"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(DiskUsageDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryKey()
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
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDiskUsageHints_FollowTheLanguage_AndFallBackToEnglish()
    {
        UiText.Current = null;
        Dialog? english = DialogHintsWholeTest.Resolve(typeof(DiskUsageDialog), 0) as Dialog;
        Assert.IsNotNull(english);
        Assert.IsTrue(english.GetKeyHints().Any(h => h.Description == "Sort by name or by size"));

        Speak("de");
        Dialog? german = DialogHintsWholeTest.Resolve(typeof(DiskUsageDialog), 0) as Dialog;
        Assert.IsNotNull(german);
        Assert.IsTrue(german.GetKeyHints().Any(h => h.Description == "Nach Name oder Größe sortieren"));
    }
}
