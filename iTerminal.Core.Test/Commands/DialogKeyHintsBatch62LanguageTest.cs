using iConsole.Dialogs;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The key hints of the sort, user menu, signature, secure wipe, find files, update, theme, translator and the remaining dialogs follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogKeyHintsBatch62LanguageTest
{
    private static readonly string[] Keys = ["DialogHint.SendQuestion","DialogHint.ClearQuestionCancelAnswer","DialogHint.RecallQuestions","DialogHint.ScrollAnswer","DialogHint.EnterDigitsOperators","DialogHint.Evaluate","DialogHint.ClearShort","DialogHint.SaveChecksumsNextToFiles","DialogHint.CopySha256","DialogHint.CopyChecksumUnderPointer","DialogHint.CloseReportCompareEmpty","DialogHint.FilterCommands","DialogHint.ChooseCommand","DialogHint.RunChosenCommand","DialogHint.CompareTwoPanels","DialogHint.SynchroniseShowsPlan","DialogHint.ScrollDifferencesPage","DialogHint.RefreshRates","DialogHint.SwapCurrencies","DialogHint.EjectDrive","DialogHint.FormatDrive","DialogHint.MountDiskImage","DialogHint.BackToDriveUnderCursor","DialogHint.ConnectChosenDevice","DialogHint.SearchDevicesAgain","DialogHint.ChooseLanguage","DialogHint.JumpFirstLastLanguage","DialogHint.UseChosenLanguage","DialogHint.PlayOrPause","DialogHint.PlayFromStart","DialogHint.SeekBackForward","DialogHint.RaiseLowerVolume","DialogHint.StopThisFile","DialogHint.EditTags","DialogHint.MoveBetweenTags","DialogHint.SaveTags","DialogHint.CloseWithoutSaving","DialogHint.FilterProcesses","DialogHint.MoveOverProcesses","DialogHint.JumpFirstLastProcess","DialogHint.NextStepInstall","DialogHint.BrowseNextStep","DialogHint.BackPreviousStep","DialogHint.TickComponent","DialogHint.SkipOffersClose","DialogHint.SearchOrShowResult","DialogHint.JumpFirstLastResult","DialogHint.SendResultsToPanel","DialogHint.CancelSearch","DialogHint.ChoosePasses","DialogHint.ChoosePreviousNextMethod","DialogHint.ArmWipe","DialogHint.ConfirmFilesErased","DialogHint.WithdrawChooseAgain","DialogHint.SignGpg","DialogHint.SignAuthenticode","DialogHint.SignPem","DialogHint.VerifySignatures","DialogHint.CloseReport","DialogHint.MoveOverSortModes","DialogHint.JumpFirstLastMode","DialogHint.SortByModeUnderCursor","DialogHint.SortByLetterAtOnce","DialogHint.ReverseSortOrder","DialogHint.DismissWarning","DialogHint.ConfirmMetadataRemoved","DialogHint.Withdraw","DialogHint.ArmRemoval","DialogHint.CloseWithoutChanging","DialogHint.PauseResumeTask","DialogHint.MoveQueuedTask","DialogHint.CancelTask","DialogHint.ChooseTheme","DialogHint.ApplyChosenTheme","DialogHint.Translate","DialogHint.SwapLanguages","DialogHint.DetectLanguage","DialogHint.ClearTextAsks","DialogHint.MoveCategoryUnitsValue","DialogHint.InstallUpdate","DialogHint.SkipVersion","DialogHint.CloseAskLater","DialogHint.MoveOverEntries","DialogHint.JumpFirstLastEntry","DialogHint.RunEntryUnderCursor","DialogHint.RunEntryMarkedKey","DialogHint.OpenWithSystemDefault","DialogHint.SwitchColour","DialogHint.ShowHideImageInfo","DialogHint.ScrollInformation","DialogHint.EditInformation","DialogHint.SaveMetadata","DialogHint.BackToStart"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(SortDialog).Assembly))]);
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
    public void TheSortHints_FollowTheLanguage_AndFallBackToEnglish()
    {
        UiText.Current = null;
        Dialog? english = DialogHintsWholeTest.Resolve(typeof(SortDialog), 0) as Dialog;
        Assert.IsNotNull(english);
        Assert.IsTrue(english.GetKeyHints().Any(h => h.Description == "Reverse the sort order"));

        Speak("de");
        Dialog? german = DialogHintsWholeTest.Resolve(typeof(SortDialog), 0) as Dialog;
        Assert.IsNotNull(german);
        Assert.IsTrue(german.GetKeyHints().Any(h => h.Description == "Die Sortierreihenfolge umkehren"));
    }
}
