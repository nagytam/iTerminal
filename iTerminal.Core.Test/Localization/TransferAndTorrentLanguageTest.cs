using System.Resources;
using System.Text.RegularExpressions;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Dialogs.Torrents;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Torrents;
using iTerminal.Core.Tasks;
using Moq;

namespace iTerminal.Core.Test.Localization;

/// <summary>
/// The space prompts of a copy and the torrent manager's state words and column headers follow the chosen language. The
/// headers and state words sit in fixed-width columns, so every translation has to fit its column. The language is a
/// static, so this runs on its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TransferAndTorrentLanguageTest
{
    /// <summary>The widest each column's text may be, as the torrent list lays out its columns.</summary>
    private static readonly (string Key, int Width)[] ColumnWidths =
    [
        ("Torrent.Col.State", 8), ("Torrent.Col.Done", 6), ("Torrent.Col.Size", 9),
        ("Torrent.Col.Down", 10), ("Torrent.Col.Up", 10), ("Torrent.Col.Peer", 4),
    ];

    private static readonly string[] PromptKeys =
    [
        "Transfer.Space.Title", "Transfer.Space.Body", "Transfer.Space.Pause", "Transfer.Space.GoOn",
        "Transfer.Paused.Title", "Transfer.Paused.Body", "Transfer.Paused.Resume",
    ];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    /// <summary>The application's two resource sets, as the app registers them.</summary>
    private static Localizer LocalizerFor(string code)
    {
        Localizer localizer = new(
        [
            new ResourceLocalizationSource(new ResourceManager("iConsole.Localization.Resources.Strings", typeof(iConsole.Localization.Localizer).Assembly)),
            new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(TorrentManagerDialog).Assembly)),
        ]);
        Assert.IsTrue(localizer.SetLanguage(code), code);
        return localizer;
    }

    private static void Speak(string code) => UiText.Current = LocalizerFor(code);

    private static Mock<IErrorPromptService> RecordingPrompts(List<(string Title, string Message, string Accept, string Decline)> asked, params bool[] answers)
    {
        Queue<bool> queue = new(answers);
        Mock<IErrorPromptService> prompts = new();
        prompts.Setup(p => p.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, CancellationToken>((title, message, accept, decline, _) => asked.Add((title, message, accept, decline)))
            .ReturnsAsync(() => queue.Dequeue());
        return prompts;
    }

    /// <summary>A target that reports 100 bytes free, so a 500-byte copy is short.</summary>
    private static (Mock<IFileSystem> Fs, Url Source, Url Target) ShortTarget()
    {
        Mock<IFileSystem> fs = new();
        fs.As<IFreeSpaceFileSystem>().Setup(s => s.GetFreeBytes(It.IsAny<Url>())).Returns(100);
        return (fs, new Url(Path.Combine(Path.GetTempPath(), "a.bin")), new Url(Path.Combine(Path.GetTempPath(), "out", "a.bin")));
    }

    [TestMethod]
    public async Task TheSpacePrompt_SpeaksTheChosenLanguage()
    {
        Speak("de");
        var (fs, source, target) = ShortTarget();
        List<(string Title, string Message, string Accept, string Decline)> asked = [];
        var guard = TransferSpaceGuard.For(fs.Object, source, fs.Object, target, deletesSource: false, RecordingPrompts(asked, false).Object);

        await guard!.EnsureAsync(() => 500, CancellationToken.None);

        Assert.AreEqual("Nicht genug freier Speicherplatz", asked[0].Title);
        Assert.AreEqual("Anhalten", asked[0].Accept);
        Assert.AreEqual("Trotzdem fortfahren", asked[0].Decline);
        Assert.IsTrue(asked[0].Message.Contains("Am Ziel sind", StringComparison.Ordinal), asked[0].Message);
        Assert.IsTrue(asked[0].Message.Contains("zu wenig", StringComparison.Ordinal), asked[0].Message);
        Assert.IsTrue(asked[0].Message.Contains("a.bin", StringComparison.Ordinal), asked[0].Message);
    }

    [TestMethod]
    public async Task ThePausedPrompt_SpeaksTheChosenLanguage_AndNamesTheTarget()
    {
        Speak("fr");
        var (fs, source, target) = ShortTarget();
        List<(string Title, string Message, string Accept, string Decline)> asked = [];
        var guard = TransferSpaceGuard.For(fs.Object, source, fs.Object, target, deletesSource: false, RecordingPrompts(asked, true, false).Object);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => guard!.EnsureAsync(() => 500, CancellationToken.None));

        Assert.AreEqual("Transfert en pause", asked[1].Title);
        Assert.AreEqual("Reprendre", asked[1].Accept);
        Assert.AreEqual("Annuler", asked[1].Decline);
        Assert.IsTrue(asked[1].Message.Contains("a.bin", StringComparison.Ordinal), asked[1].Message);
    }

    [TestMethod]
    public void EveryLanguage_TranslatesThePrompts_AndKeepsTheirPlaceholders()
    {
        Speak("en");
        Dictionary<string, string> english = PromptKeys.ToDictionary(k => k, k => UiText.Get(k, string.Empty));
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Speak(language.Code);
            foreach (string key in PromptKeys)
            {
                string text = UiText.Get(key, string.Empty);
                Assert.IsTrue(text.Length > 0, language.Code + " " + key);
                CollectionAssert.AreEquivalent(
                    Regex.Matches(english[key], @"\{\d\}").Select(m => m.Value).ToArray(),
                    Regex.Matches(text, @"\{\d\}").Select(m => m.Value).ToArray(),
                    language.Code + " " + key);
            }
        }
    }

    [TestMethod]
    public void EveryLanguage_TheTorrentRemoveQuestionKeepsItsNameAndItsKeys()
    {
        // The answer keys are the letters the dialog reads, so the translated question must still name them.
        foreach (LanguageInfo language in Localizer.SupportedLanguages)
        {
            Speak(language.Code);
            string question = UiText.Get("Torrent.Confirm.Remove", string.Empty);

            Assert.IsTrue(question.Contains("{0}", StringComparison.Ordinal), language.Code);
            Assert.IsTrue(question.Contains(" y ", StringComparison.Ordinal), language.Code + ": " + question);
            Assert.IsTrue(question.Contains(" w ", StringComparison.Ordinal), language.Code + ": " + question);
        }
    }

    [TestMethod]
    public void EveryLanguage_TorrentStateWordsAndHeadersFitTheirColumns()
    {
        foreach (LanguageInfo language in Localizer.SupportedLanguages)
        {
            Speak(language.Code);
            foreach (TorrentStatus status in Enum.GetValues<TorrentStatus>())
            {
                string label = TorrentManagerDialog.StatusLabel(status);
                Assert.IsTrue(label.Length <= 8, $"{language.Code}: '{label}' is wider than the state column");
            }

            foreach ((string key, int width) in ColumnWidths)
            {
                string text = UiText.Get(key, string.Empty);
                Assert.IsTrue(text.Length > 0 && text.Length <= width, $"{language.Code}: '{text}' does not fit {width} columns ({key})");
            }
        }
    }
}
