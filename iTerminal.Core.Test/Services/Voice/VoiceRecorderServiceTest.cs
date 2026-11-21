using iConsole.Media.Speech;
using iTerminal.Core.Services.Voice;
using System.Text;

namespace iTerminal.Core.Test.Services.Voice;

[TestClass]
public class VoiceRecorderServiceTest
{
    private sealed class FakeCapture(byte[] pcm) : IMicrophoneCapture
    {
        public volatile bool Started;
        public volatile bool Disposed;

        public void Start() => Started = true;

        public byte[] Stop() => pcm;

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeFactory(IMicrophoneCapture capture) : IMicrophoneCaptureFactory
    {
        public int Created;

        public IMicrophoneCapture Create()
        {
            Interlocked.Increment(ref Created);
            return capture;
        }
    }

    private sealed class FakeRecognizer : ISpeechRecognizer
    {
        public bool Installed = true;
        public int Calls;
        public Func<CancellationToken, Task<string>> Answer = _ => Task.FromResult("hello there");

        public bool IsModelInstalled => Installed;

        public Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Answer(cancellationToken);
        }
    }

    private static byte[] Speech(int seconds = 1)
    {
        byte[] pcm = new byte[Pcm.BytesPerSecond * seconds];
        for (int i = 0; i < pcm.Length; i += 2)
        {
            short sample = (short)(i % 200 < 100 ? 4000 : -4000);
            pcm[i] = (byte)(sample & 0xFF);
            pcm[i + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return pcm;
    }

    private readonly List<string> _folders = [];

    /// <summary>A folder of this test only; <see cref="Cleanup"/> removes it when the test ends.</summary>
    private string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "VoiceRecorderTest_" + Guid.NewGuid().ToString("N")[..8]);
        _folders.Add(folder);
        return folder;
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AStoppedRecordingIsSavedAsAWavFileWithoutTranscription()
    {
        string folder = NewFolder();
        var capture = new FakeCapture(Speech(2));
        var recognizer = new FakeRecognizer();
        using var service = new VoiceRecorderService(new FakeFactory(capture), recognizer, () => folder);

        service.Start();
        VoiceRecording? recording = await service.StopAsync(transcribe: false, CancellationToken.None);

        Assert.IsNotNull(recording);
        Assert.IsTrue(capture.Started);
        Assert.IsTrue(capture.Disposed);
        Assert.AreEqual(0, recognizer.Calls);
        Assert.IsNull(recording.Transcript);
        Assert.IsNull(recording.TranscriptPath);
        Assert.IsNull(recording.Problem);
        Assert.AreEqual(2, recording.Duration.TotalSeconds, 0.01);
        Assert.AreEqual(folder, Path.GetDirectoryName(recording.WavPath));
        Assert.IsTrue(Path.GetFileName(recording.WavPath).StartsWith("voice-", StringComparison.Ordinal));

        byte[] file = await File.ReadAllBytesAsync(recording.WavPath);
        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(file, 0, 4));
        Assert.AreEqual(44 + Speech(2).Length, file.Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithTranscriptionTheTextIsReturnedAndSavedBesideTheWavFile()
    {
        string folder = NewFolder();
        var recognizer = new FakeRecognizer();
        using var service = new VoiceRecorderService(new FakeFactory(new FakeCapture(Speech())), recognizer, () => folder);

        service.Start();
        VoiceRecording? recording = await service.StopAsync(transcribe: true, CancellationToken.None);

        Assert.IsNotNull(recording);
        Assert.AreEqual(1, recognizer.Calls);
        Assert.AreEqual("hello there", recording.Transcript);
        Assert.IsNotNull(recording.TranscriptPath);
        Assert.AreEqual(Path.ChangeExtension(recording.WavPath, ".txt"), recording.TranscriptPath);
        Assert.AreEqual("hello there", await File.ReadAllTextAsync(recording.TranscriptPath));
        Assert.IsNull(recording.Problem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutTheModelTheRecordingIsKeptAndTheProblemIsSaid()
    {
        string folder = NewFolder();
        var recognizer = new FakeRecognizer { Installed = false };
        using var service = new VoiceRecorderService(new FakeFactory(new FakeCapture(Speech())), recognizer, () => folder);

        service.Start();
        VoiceRecording? recording = await service.StopAsync(transcribe: true, CancellationToken.None);

        Assert.IsNotNull(recording);
        Assert.AreEqual(0, recognizer.Calls);
        Assert.IsTrue(File.Exists(recording.WavPath));
        Assert.IsNull(recording.Transcript);
        Assert.IsNull(recording.TranscriptPath);
        Assert.IsNotNull(recording.Problem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedTranscriptionKeepsTheRecordingAndSaysWhy()
    {
        string folder = NewFolder();
        var recognizer = new FakeRecognizer
        {
            Answer = _ => throw new SpeechException("the engine failed"),
        };
        using var service = new VoiceRecorderService(new FakeFactory(new FakeCapture(Speech())), recognizer, () => folder);

        service.Start();
        VoiceRecording? recording = await service.StopAsync(transcribe: true, CancellationToken.None);

        Assert.IsNotNull(recording);
        Assert.IsTrue(File.Exists(recording.WavPath));
        Assert.IsNull(recording.Transcript);
        Assert.AreEqual("the engine failed", recording.Problem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARecordingShorterThanTheMinimumIsNotKept()
    {
        string folder = NewFolder();
        using var service = new VoiceRecorderService(new FakeFactory(new FakeCapture(Speech(0))), new FakeRecognizer(), () => folder);

        service.Start();
        VoiceRecording? recording = await service.StopAsync(transcribe: false, CancellationToken.None);

        Assert.IsNull(recording);
        Assert.IsFalse(Directory.Exists(folder));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StopWithoutAStartReturnsNothing()
    {
        using var service = new VoiceRecorderService(new FakeFactory(new FakeCapture(Speech())), new FakeRecognizer(), NewFolder);

        Assert.IsNull(await service.StopAsync(transcribe: false, CancellationToken.None));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DiscardReleasesTheMicrophoneWithoutSaving()
    {
        string folder = NewFolder();
        var capture = new FakeCapture(Speech());
        using var service = new VoiceRecorderService(new FakeFactory(capture), new FakeRecognizer(), () => folder);

        service.Start();
        service.Discard();

        WaitUntil(() => capture.Disposed);
        Assert.IsTrue(capture.Disposed);
        Assert.IsFalse(Directory.Exists(folder));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TwoRecordingsInTheSameSecondDoNotOverwriteEachOther()
    {
        string folder = NewFolder();
        using var service = new VoiceRecorderService(new FakeFactory(new FakeCapture(Speech())), new FakeRecognizer(), () => folder);

        service.Start();
        VoiceRecording? first = await service.StopAsync(transcribe: false, CancellationToken.None);
        service.Start();
        VoiceRecording? second = await service.StopAsync(transcribe: false, CancellationToken.None);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreNotEqual(first.WavPath, second.WavPath);
        Assert.IsTrue(File.Exists(first.WavPath));
        Assert.IsTrue(File.Exists(second.WavPath));
    }

    private static void WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            Thread.Sleep(10);
        }
    }
}
