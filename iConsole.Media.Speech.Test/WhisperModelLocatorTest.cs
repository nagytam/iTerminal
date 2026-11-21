using iConsole.Media.Speech;

namespace iConsole.Media.Speech.Test;

/// <summary>The model comes from the application through <see cref="IWhisperModelLocator"/>: without one, the recogniser says so.</summary>
[TestClass]
public class WhisperModelLocatorTest
{
    private sealed class NoModel : IWhisperModelLocator
    {
        public string? ModelPath() => null;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAModel_TheRecogniserReportsItNotInstalled()
    {
        var recognizer = new WhisperRecognizer(new NoModel());

        Assert.IsFalse(recognizer.IsModelInstalled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutAModel_TranscribingRefusesWithTheModelMessage()
    {
        var recognizer = new WhisperRecognizer(new NoModel());

        SpeechException error = await Assert.ThrowsExactlyAsync<SpeechException>(
            () => recognizer.TranscribeAsync(new float[16], CancellationToken.None));

        Assert.AreEqual("Voice input needs the Whisper model. Install it from Tools > Runtime Dependencies.", error.Message);
    }
}
