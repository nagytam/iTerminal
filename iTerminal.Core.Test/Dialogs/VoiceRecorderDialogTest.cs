using iConsole.Data;
using iConsole.Media.Speech;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Voice;
using iTerminal.Core.Services.Voice;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class VoiceRecorderDialogTest
{
    private sealed class FakeRecorder : IVoiceRecorderService
    {
        public bool IsModelInstalled { get; set; } = true;
        public SpeechException? StartFails { get; set; }
        public SpeechException? StopFails { get; set; }
        public volatile bool Discarded;
        public volatile bool Stopped;

        public void Start()
        {
            if (StartFails is not null)
            {
                throw StartFails;
            }
        }

        public Task<VoiceRecording?> StopAsync(bool transcribe, CancellationToken cancellationToken)
        {
            Stopped = true;
            if (StopFails is not null)
            {
                return Task.FromException<VoiceRecording?>(StopFails);
            }
            string wav = Path.Combine(Path.GetTempPath(), "voice-test.wav");
            return Task.FromResult<VoiceRecording?>(transcribe
                ? new VoiceRecording(wav, TimeSpan.FromSeconds(2), "hello there", Path.ChangeExtension(wav, ".txt"), null)
                : new VoiceRecording(wav, TimeSpan.FromSeconds(2), null, null, null));
        }

        public void Discard() => Discarded = true;

        public void Dispose()
        {
        }
    }

    private static VoiceRecorderDialog NewDialog(FakeRecorder recorder) => new(recorder, TestTheme());

    [Timeout(60_000)]
    [TestMethod]
    public async Task BeforeARecordingItSaysHowToStartAndThatTranscriptionIsOff()
    {
        var dialog = NewDialog(new FakeRecorder());
        await ShowAsync(dialog);

        var screen = Render(dialog);
        AssertScreenContains(screen, "Voice recorder");
        AssertScreenContains(screen, "Enter starts recording.");
        AssertScreenContains(screen, "AI transcription: off");
        AssertScreenContains(screen, "Enter:record  T:AI transcription  Esc:close");
        Assert.IsFalse(dialog.IsRecording);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterStartsAndEnterAgainStopsAndSavesTheRecording()
    {
        var recorder = new FakeRecorder();
        var dialog = NewDialog(recorder);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;

        var screen = Render(dialog);
        AssertScreenContains(screen, "Recording 00:00.");
        AssertScreenContains(screen, "Enter:stop and save  Esc:discard");
        Assert.IsTrue(dialog.IsRecording);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;

        Assert.IsTrue(recorder.Stopped);
        Assert.IsNotNull(dialog.LastRecording);
        screen = Render(dialog);
        AssertScreenContains(screen, "Saved:");
        AssertScreenContains(screen, "Length: 00:02");
        AssertScreenContains(screen, "Enter:record");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithTranscriptionOnTheTextIsShownUnderTheRecording()
    {
        var dialog = NewDialog(new FakeRecorder());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.T));
        Assert.IsTrue(dialog.TranscriptionOn);
        AssertScreenContains(Render(dialog), "AI transcription: on");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;

        var screen = Render(dialog);
        AssertScreenContains(screen, "Transcript");
        AssertScreenContains(screen, "hello there");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TranscriptionCannotBeSwitchedOnWithoutTheModel()
    {
        var dialog = NewDialog(new FakeRecorder { IsModelInstalled = false });
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.T));

        Assert.IsFalse(dialog.TranscriptionOn);
        AssertScreenContains(Render(dialog), "model not installed");
        AssertScreenContains(Render(dialog), "Voice input needs the Whisper model.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscDuringARecordingDiscardsItAndKeepsTheDialogOpen()
    {
        var recorder = new FakeRecorder();
        var dialog = NewDialog(recorder);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(recorder.Discarded);
        Assert.IsFalse(recorder.Stopped);
        Assert.IsNull(dialog.LastRecording);
        AssertScreenContains(Render(dialog), "Recording discarded.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMicrophoneThatCannotOpenSaysWhy()
    {
        var dialog = NewDialog(new FakeRecorder { StartFails = new SpeechException("No microphone here.") });
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;

        Assert.IsFalse(dialog.IsRecording);
        AssertScreenContains(Render(dialog), "No microphone here.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARecorderThatFailsWhileStoppingSaysWhy()
    {
        var dialog = NewDialog(new FakeRecorder { StopFails = new SpeechException("The microphone was revoked.") });
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;

        Assert.IsFalse(dialog.IsRecording);
        Assert.IsNull(dialog.LastRecording);
        AssertScreenContains(Render(dialog), "The microphone was revoked.");
    }

    [TestMethod]
    public void TheWrapBreaksAtSpacesAndCutsWordsLongerThanTheLine()
    {
        CollectionAssert.AreEqual(new[] { "one two", "three" }, VoiceRecorderDialog.Wrap("one two three", 8));
        CollectionAssert.AreEqual(new[] { "abcd", "efgh", "ij" }, VoiceRecorderDialog.Wrap("abcdefghij", 4));
        CollectionAssert.AreEqual(Array.Empty<string>(), VoiceRecorderDialog.Wrap("   ", 10));
    }

    [TestMethod]
    public void TheCommandHasNoHotKey()
    {
        var command = new VoiceRecorderDialogCommand(NewDialog(new FakeRecorder()));

        Assert.IsNull(command.Key);
    }
}
