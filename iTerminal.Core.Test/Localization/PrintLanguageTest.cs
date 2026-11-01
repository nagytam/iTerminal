using System.Resources;
using iConsole.Localization;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Printing;

namespace iTerminal.Core.Test.Localization;

/// <summary>
/// The print dialog follows the chosen language, and its columns are fixed-width, so every translation of a column
/// header or a job state has to fit. The language is a static, so this runs on its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PrintLanguageTest
{
    private static readonly string[] Languages = ["en", "de", "fr", "es", "it", "hu"];

    /// <summary>The widest each column's text may be, as the queue list lays out its columns.</summary>
    private static readonly (string Key, int Width)[] ColumnWidths =
    [
        ("Print.Col.Id", 5), ("Print.Col.Owner", 10), ("Print.Col.State", 8),
        ("Print.State.Printing", 8), ("Print.State.Held", 8), ("Print.State.Error", 8), ("Print.State.Queued", 8),
        ("Print.File", 11), ("Print.Printer", 11), ("Print.Copies", 11),
    ];

    private static readonly string[] AllKeys =
    [
        "Menu.Print", "Menu.PrintItem", "DialogHint.PrintFile", "DialogHint.ChoosePrinter", "DialogHint.Copies", "DialogHint.ChooseJob",
        "DialogHint.HoldOrResumeJob", "DialogHint.CancelJobAsks", "DialogHint.RefreshQueue", "DialogHint.CancelJobYes", "DialogHint.KeepJob",
        "Print.Status.AskCancel", "Print.Status.NoFile", "Print.Status.NoPrinter", "Print.Status.Sending", "Print.NoFile", "Print.Col.Document",
        "Print.QueueEmpty", "Print.Reading", "Print.NoPrinters", "Print.Default", "Print.Hint.Confirm", "Print.Hint.List",
        .. ColumnWidths.Select(c => c.Key),
    ];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer LocalizerFor(string code)
    {
        Localizer localizer = new(
        [
            new ResourceLocalizationSource(new ResourceManager("iConsole.Localization.Resources.Strings", typeof(Localizer).Assembly)),
            new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(PrintDialog).Assembly)),
        ]);
        Assert.IsTrue(localizer.SetLanguage(code), code);
        return localizer;
    }

    [TestMethod]
    public void EveryKey_IsTranslatedInEveryLanguage()
    {
        foreach (var code in Languages)
        {
            var localizer = LocalizerFor(code);
            foreach (var key in AllKeys.Distinct())
            {
                Assert.IsTrue(localizer.TryGet(key, out _), $"{key} is missing in {code}");
            }
        }
    }

    [TestMethod]
    public void TheColumnsAndLabels_FitTheirWidth_InEveryLanguage()
    {
        foreach (var code in Languages)
        {
            var localizer = LocalizerFor(code);
            foreach (var (key, width) in ColumnWidths)
            {
                var text = localizer.Get(key)!;
                Assert.IsTrue(text.Length <= width, $"{key} in {code} is '{text}' ({text.Length} > {width})");
            }
        }
    }

    [TestMethod]
    public void TheDialogSpeaksTheChosenLanguage()
    {
        UiText.Current = LocalizerFor("de");

        Assert.AreEqual("Pausiert", PrintDialog.StateLabel(PrintJobState.Held));
        Assert.AreEqual("Wartend", PrintDialog.StateLabel(PrintJobState.Queued));

        UiText.Current = LocalizerFor("en");

        Assert.AreEqual("Held", PrintDialog.StateLabel(PrintJobState.Held));
    }

    [TestMethod]
    public void TheMessagesWithPlaceholders_KeepThem_InEveryLanguage()
    {
        foreach (var code in Languages)
        {
            var localizer = LocalizerFor(code);
            StringAssert.Contains(localizer.Get("Print.Status.AskCancel")!, "{0}", code);
            StringAssert.Contains(localizer.Get("Print.Status.AskCancel")!, "{1}", code);
            StringAssert.Contains(localizer.Get("Print.Status.Sending")!, "{0}", code);
            StringAssert.Contains(localizer.Get("Print.Status.Sending")!, "{1}", code);
        }
    }
}
