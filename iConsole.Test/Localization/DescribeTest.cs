using iConsole.Commands;
using iConsole.Localization;

namespace iConsole.Test.Localization;

/// <summary>A command's description is looked up by its English text, so no command needs a key of its own.</summary>
[TestClass]
public class DescribeTest
{
    private IDisposable? _language;

    [TestCleanup]
    public void Reset() => _language?.Dispose();

    private void Speak(string code)
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage(code));
        _language?.Dispose();
        _language = UiText.Scope(localizer);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheKey_IsTheSameForTheSameSentence_AndIgnoresSurroundingSpace()
    {
        string key = UiText.DescriptionKey("Exit the application.");

        Assert.AreEqual(key, UiText.DescriptionKey("  Exit the application.\r\n"));
        Assert.AreNotEqual(key, UiText.DescriptionKey("Exit the program."));
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(key, "^CmdDesc\\.[0-9a-f]{8}$"), key);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithNoLanguage_OrAnUnknownSentence_TheEnglishComesBack()
    {
        _language?.Dispose();
        _language = UiText.Scope(null);
        Assert.AreEqual("Exit the application.", UiText.Describe("Exit the application."));

        Speak("de");
        Assert.AreEqual("A sentence no language has.", UiText.Describe("A sentence no language has."));
        Assert.AreEqual(string.Empty, UiText.Describe(string.Empty));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFrameworkCommandsDescription_FollowsTheLanguage()
    {
        string english = new QuitCommand().Description;

        Speak("de");
        Assert.AreEqual("Die Anwendung beenden.", UiText.Describe(english));
        Speak("fr");
        Assert.AreEqual("Quitter l’application.", UiText.Describe(english));
        Speak("hu");
        Assert.AreEqual("Kilépés az alkalmazásból.", UiText.Describe(english));
    }
}
