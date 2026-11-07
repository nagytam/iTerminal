using iTerminal.Core.Speech;

namespace iTerminal.Core.Test.Speech;

[TestClass]
public class WhisperRecognizerTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("[BLANK_AUDIO]")]
    [DataRow(" [ Silence ] ")]
    [DataRow("(music)")]
    [DataRow("*applause*")]
    [DataRow("")]
    [DataRow("   ")]
    public void AMarkerForWhatIsNotSpeech_IsDropped(string segment) =>
        Assert.IsTrue(WhisperRecognizer.IsNonSpeech(segment));

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(" What is the weather")]
    [DataRow(" Open [the] folder")]
    [DataRow("(and) then more words (here)")]
    [DataRow(" [first] and [second]")]
    [DataRow("a")]
    public void SpeechIsKept_EvenWithBrackets(string segment) =>
        Assert.IsFalse(WhisperRecognizer.IsNonSpeech(segment));
}
