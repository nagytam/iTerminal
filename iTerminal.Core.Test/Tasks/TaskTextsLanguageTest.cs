using System.Text.RegularExpressions;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Tasks;

/// <summary>The progress lines of the copy, move, delete, compress, extract, scan, discovery and synchronise tasks follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class TaskTextsLanguageTest
{
    private static readonly string[] Keys = ["Task.Started","Task.Finished","Task.Cancelled","Task.VerbFailed","Task.VerbStep","Task.BatchCancelled","Task.BatchFinished1","Task.BatchFinished2","Task.Compress","Task.ReadingFiles","Task.FetchingArchiveToAdd","Task.CompressingInto","Task.Uploading","Task.CompressCancelled","Task.CompressFailed","Task.Delete","Task.DeleteFailed","Task.WithErrors","Task.VerbFile","Task.VerbFolder","Task.To","Task.BytesCopied","Task.VerbFileFailed","Task.VerbFolderFailed","Task.ProgressFiles","Task.RemovingSource","Task.WithErrorsSuffix","Task.ExtractCancelled","Task.FilesWritten","Task.ExtractStep","Task.FetchingArchive","Task.ExtractStepNamed","Task.Scanning","Task.Scanned","Task.ScanOf","Task.ScanFailed","Task.FoundSoFar","Task.Discovering","Task.Discovered","Task.DiscoveryOf","Task.DiscoveryFailed","Task.Synchronise","Task.SyncCancelled","Verb.Copy","Verb.Move","Verb.Copying","Verb.Moving"];

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
        key == "Task.VerbFile" && code == "it";

    private static string[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().Order()];

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

    [TestMethod]
    public void TheTaskLines_FillTheirPlaceholders_InEveryLanguage()
    {
        UiText.Current = null;
        Assert.AreEqual("Extract 2 of 5", string.Format(UiText.Get("Task.ExtractStep", "Extract {0} of {1}"), 2, 5));

        Speak("de");
        Assert.AreEqual("Entpacken 2 von 5", string.Format(UiText.Get("Task.ExtractStep", "?"), 2, 5));
    }
}
