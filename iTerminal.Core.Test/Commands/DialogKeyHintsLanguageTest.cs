using iConsole.Dialogs;
using iConsole.Localization;
using iTerminal.Core.Dialogs.Contacts;

namespace iTerminal.Core.Test.Commands;

/// <summary>The key hints of the contacts, access control, web browser and mail dialogs follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogKeyHintsLanguageTest
{
    private static readonly string[] Keys = ["DialogHint.NextFieldEnterSaves","DialogHint.PreviousField","DialogHint.MoveBetweenFields","DialogHint.KeepFilterBackToList","DialogHint.ClearFilter","DialogHint.DeleteContact","DialogHint.KeepContact","DialogHint.MoveOverContacts","DialogHint.JumpFirstLastContact","DialogHint.AddContact","DialogHint.ChangeContact","DialogHint.DeleteContactAsks","DialogHint.FilterList","DialogHint.SaveEntry","DialogHint.ChangeChoice","DialogHint.BackToList","DialogHint.SaveOwner","DialogHint.DeleteEntry","DialogHint.KeepEntry","DialogHint.ChooseEntry","DialogHint.AddEntry","DialogHint.EditEntry","DialogHint.DeleteEntryAsks","DialogHint.SwitchInheritance","DialogHint.ChangeOwner","DialogHint.ReadPermissionsAgain","DialogHint.BackToPage","DialogHint.TypeAddress","DialogHint.NextLinkOrField","DialogHint.FollowLinkNewTab","DialogHint.ScrollPageUpDown","DialogHint.ScrollPageDownShiftUp","DialogHint.Forward","DialogHint.Reload","DialogHint.OpenCloseTab","DialogHint.NextTab","DialogHint.BookmarkPage","DialogHint.BookmarksHistory","DialogHint.FindInPage","DialogHint.ChooseMessage","DialogHint.ReadMessage","DialogHint.WriteMessage","DialogHint.RefreshInbox","DialogHint.DeleteMessageAsks","DialogHint.AccountSettings","DialogHint.ScrollMessage","DialogHint.Reply","DialogHint.BackToInbox","DialogHint.SendMessage","DialogHint.LookUpAddress"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(ContactManagerDialog).Assembly))]);
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
    public void TheContactManagerHints_FollowTheLanguage_AndFallBackToEnglish()
    {
        UiText.Current = null;
        Dialog? english = DialogHintsWholeTest.Resolve(typeof(ContactManagerDialog), 0) as Dialog;
        Assert.IsNotNull(english);
        Assert.IsTrue(english.GetKeyHints().Any(h => h.Description == "Add a contact"));

        Speak("de");
        Dialog? german = DialogHintsWholeTest.Resolve(typeof(ContactManagerDialog), 0) as Dialog;
        Assert.IsNotNull(german);
        Assert.IsTrue(german.GetKeyHints().Any(h => h.Description == "Einen Kontakt hinzufügen"));
    }
}
