using iConsole.Services;
using iTerminal.Core.Speech;
using Moq;

namespace iTerminal.Core.Test.Speech;

[TestClass]
public class SpeechToTextServiceTest
{
    private sealed class FakeCapture(byte[] pcm, SpeechException? startFails = null) : IMicrophoneCapture
    {
        public volatile bool Started;
        public volatile bool Stopped;
        public volatile bool Disposed;

        public void Start()
        {
            if (startFails is not null)
            {
                throw startFails;
            }

            Started = true;
        }

        public byte[] Stop()
        {
            Stopped = true;
            return pcm;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeFactory(FakeCapture capture) : IMicrophoneCaptureFactory
    {
        public IMicrophoneCapture Create() => capture;
    }

    private sealed class FakeRecognizer : ISpeechRecognizer
    {
        public bool Installed = true;
        public int Calls;
        public Func<float[], CancellationToken, Task<string>> Answer = (_, _) => Task.FromResult("hello there");

        public bool IsModelInstalled => Installed;

        public Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Answer(samples, cancellationToken);
        }
    }

    private static byte[] Speech(int seconds = 1)
    {
        byte[] pcm = new byte[Pcm.BytesPerSecond * seconds];
        for (int i = 0; i < pcm.Length; i += 2)
        {
            pcm[i + 1] = 0x20;
        }

        return pcm;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "Timed out waiting for " + because);
            await Task.Delay(10);
        }
    }

    private static (SpeechToTextService Service, FakeCapture Capture, FakeRecognizer Recognizer, Mock<INotificationService> Notifications) Create(
        byte[]? pcm = null, SpeechException? startFails = null, TimeSpan? limit = null)
    {
        var capture = new FakeCapture(pcm ?? Speech(), startFails);
        var recognizer = new FakeRecognizer();
        var notifications = new Mock<INotificationService>();
        var service = new SpeechToTextService(new FakeFactory(capture), recognizer, notifications.Object, null, limit ?? TimeSpan.FromSeconds(30));
        return (service, capture, recognizer, notifications);
    }

    private static void VerifyNotified(Mock<INotificationService> notifications, string text, Times times) =>
        notifications.Verify(n => n.Notify(It.IsAny<string>(), It.Is<string?>(m => m != null && m.Contains(text)), NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), times);

    [Timeout(60_000)]
    [TestMethod]
    public async Task ToggleTwice_RecordsThenTranscribes_AndHandsTheTextOverOnce()
    {
        var (service, capture, _, _) = Create();

        service.Toggle();
        Assert.AreEqual(SpeechState.Recording, service.State);
        await WaitUntilAsync(() => capture.Started, "the recorder to start");

        service.Toggle();
        await WaitUntilAsync(() => service.State == SpeechState.Idle, "the transcription to finish");

        Assert.IsTrue(capture.Stopped);
        Assert.IsTrue(capture.Disposed);
        Assert.AreEqual("hello there", service.TakeResult());
        Assert.IsNull(service.TakeResult(), "a result is handed over once");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleReturnsAtOnce_EvenWhenTheRecorderIsSlowToOpen()
    {
        var gate = new ManualResetEventSlim();
        var recognizer = new FakeRecognizer();
        var service = new SpeechToTextService(new SlowFactory(gate), recognizer);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        service.Toggle();
        clock.Stop();
        gate.Set();
        service.Dispose();

        Assert.IsTrue(clock.ElapsedMilliseconds < 200, $"Toggle took {clock.ElapsedMilliseconds} ms");
    }

    private sealed class SlowFactory(ManualResetEventSlim gate) : IMicrophoneCaptureFactory
    {
        public IMicrophoneCapture Create()
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return new FakeCapture(Speech());
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutTheModel_ItSaysSo_AndRecordsNothing()
    {
        var (service, capture, recognizer, notifications) = Create();
        recognizer.Installed = false;

        service.Toggle();

        Assert.AreEqual(SpeechState.Idle, service.State);
        Assert.IsFalse(capture.Started);
        VerifyNotified(notifications, "Whisper model", Times.Once());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Cancel_WhileRecording_DiscardsTheAudio()
    {
        var (service, capture, recognizer, _) = Create();
        service.Toggle();
        await WaitUntilAsync(() => capture.Started, "the recorder to start");

        service.Cancel();

        Assert.AreEqual(SpeechState.Idle, service.State);
        await WaitUntilAsync(() => capture.Disposed, "the recorder to be released");
        Assert.AreEqual(0, recognizer.Calls);
        Assert.IsNull(service.TakeResult());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Cancel_WhileTranscribing_DropsTheLateAnswer()
    {
        var (service, capture, recognizer, _) = Create();
        var release = new TaskCompletionSource<string>();
        recognizer.Answer = (_, _) => release.Task;
        service.Toggle();
        await WaitUntilAsync(() => capture.Started, "the recorder to start");
        service.Toggle();
        await WaitUntilAsync(() => service.State == SpeechState.Transcribing, "the transcription to start");

        service.Cancel();
        release.SetResult("too late");
        await Task.Delay(100);

        Assert.AreEqual(SpeechState.Idle, service.State);
        Assert.IsNull(service.TakeResult());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AToggleWhileTranscribing_IsIgnored()
    {
        var (service, capture, recognizer, _) = Create();
        var release = new TaskCompletionSource<string>();
        recognizer.Answer = (_, _) => release.Task;
        service.Toggle();
        await WaitUntilAsync(() => capture.Started, "the recorder to start");
        service.Toggle();
        await WaitUntilAsync(() => service.State == SpeechState.Transcribing, "the transcription to start");

        service.Toggle();
        Assert.AreEqual(SpeechState.Transcribing, service.State);

        release.SetResult("done");
        await WaitUntilAsync(() => service.State == SpeechState.Idle, "the transcription to finish");
        Assert.AreEqual("done", service.TakeResult());
        Assert.AreEqual(1, recognizer.Calls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheTimeLimit_EndsTheRecordingByItself()
    {
        var (service, capture, _, _) = Create(limit: TimeSpan.FromMilliseconds(150));

        service.Toggle();

        await WaitUntilAsync(() => service.State == SpeechState.Idle && capture.Stopped, "the limit to end the recording");
        Assert.AreEqual("hello there", service.TakeResult());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARecordingShorterThanAKeyBounce_IsNotRecognised()
    {
        var (service, capture, recognizer, _) = Create(pcm: new byte[Pcm.MinBytes - 2]);
        service.Toggle();
        await WaitUntilAsync(() => capture.Started, "the recorder to start");

        service.Toggle();
        await WaitUntilAsync(() => capture.Stopped && service.State == SpeechState.Idle, "the recording to end");

        Assert.AreEqual(0, recognizer.Calls);
        Assert.IsNull(service.TakeResult());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASilentMicrophone_IsReported()
    {
        var (service, capture, recognizer, notifications) = Create(pcm: new byte[Pcm.BytesPerSecond]);
        service.Toggle();
        await WaitUntilAsync(() => capture.Started, "the recorder to start");

        service.Toggle();
        await WaitUntilAsync(() => service.State == SpeechState.Idle && capture.Stopped, "the recording to end");

        Assert.AreEqual(0, recognizer.Calls);
        VerifyNotified(notifications, "silence", Times.Once());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARecorderThatCannotStart_IsReported_AndTheServiceIsIdleAgain()
    {
        var (service, capture, _, notifications) = Create(startFails: new SpeechException("No microphone was found."));

        service.Toggle();

        await WaitUntilAsync(() => service.State == SpeechState.Idle, "the failure to be handled");
        VerifyNotified(notifications, "No microphone", Times.Once());
        Assert.IsTrue(capture.Disposed);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEngineThatFails_IsReported_AndNothingIsTyped()
    {
        var (service, capture, recognizer, notifications) = Create();
        recognizer.Answer = (_, _) => throw new SpeechException("The speech engine could not be loaded on this system");
        service.Toggle();
        await WaitUntilAsync(() => capture.Started, "the recorder to start");

        service.Toggle();

        await WaitUntilAsync(() => service.State == SpeechState.Idle && capture.Stopped, "the failure to be handled");
        VerifyNotified(notifications, "speech engine", Times.Once());
        Assert.IsNull(service.TakeResult());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Dispose_ReleasesTheMicrophone()
    {
        var (service, capture, _, _) = Create();
        service.Toggle();
        await WaitUntilAsync(() => capture.Started, "the recorder to start");

        service.Dispose();

        await WaitUntilAsync(() => capture.Disposed, "the recorder to be released");
        service.Toggle();
        Assert.AreEqual(SpeechState.Idle, service.State, "a disposed service records nothing");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EveryStateChange_RaisesChanged()
    {
        var (service, capture, _, _) = Create();
        int raised = 0;
        service.Changed += (_, _) => Interlocked.Increment(ref raised);

        service.Toggle();
        await WaitUntilAsync(() => capture.Started, "the recorder to start");
        service.Toggle();
        // Recording, transcribing, idle again with the text.
        await WaitUntilAsync(() => Volatile.Read(ref raised) >= 3, "the cycle to end");

        Assert.AreEqual("hello there", service.TakeResult());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Dispose_HasReleasedTheMicrophoneByTheTimeItReturns()
    {
        var (service, capture, _, _) = Create();
        service.Toggle();
        await WaitUntilAsync(() => capture.Started, "the recorder to start");

        service.Dispose();

        Assert.IsTrue(capture.Disposed, "a recorder child process would outlive the application otherwise");
    }

    private sealed class CountingFactory : IMicrophoneCaptureFactory
    {
        public readonly List<FakeCapture> Captures = [];

        public IMicrophoneCapture Create()
        {
            // The first recorder is still open when the second is asked for: that is the overlap being ruled out.
            Assert.IsTrue(Captures.All(c => c.Disposed), "the previous recorder must be released before a new one is opened");
            var capture = new FakeCapture(Speech());
            Captures.Add(capture);
            return capture;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARecordingStartedRightAfterACancel_WaitsForTheOldRecorderToBeReleased()
    {
        var factory = new CountingFactory();
        var service = new SpeechToTextService(factory, new FakeRecognizer(), null, null, TimeSpan.FromSeconds(30));
        service.Toggle();
        await WaitUntilAsync(() => factory.Captures.Count == 1 && factory.Captures[0].Started, "the first recorder to start");

        service.Cancel();
        service.Toggle();

        await WaitUntilAsync(() => factory.Captures.Count == 2 && factory.Captures[1].Started, "the second recorder to start");
        Assert.IsTrue(factory.Captures[0].Disposed);
        service.Dispose();
    }
}
