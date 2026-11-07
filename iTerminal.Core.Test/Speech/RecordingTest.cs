using System.Diagnostics;
using iTerminal.Core.Speech;

namespace iTerminal.Core.Test.Speech;

/// <summary>The recording format, the recorder command lines and the recorder process's life.</summary>
[TestClass]
public class RecordingTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Samples_AreScaledExactly()
    {
        // Little-endian: -32768 = 00 80, 32767 = FF 7F, 16384 = 00 40, -1 = FF FF.
        byte[] pcm = [0x00, 0x80, 0xFF, 0x7F, 0x00, 0x00, 0x00, 0x40, 0xFF, 0xFF];

        float[] samples = Pcm.ToSamples(pcm);

        Assert.AreEqual(5, samples.Length);
        Assert.AreEqual(-1.0f, samples[0]);
        Assert.AreEqual(32767f / 32768f, samples[1]);
        Assert.AreEqual(0f, samples[2]);
        Assert.AreEqual(0.5f, samples[3]);
        Assert.AreEqual(-1f / 32768f, samples[4]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ATrailingHalfSample_IsIgnored()
    {
        Assert.AreEqual(1, Pcm.ToSamples([0x00, 0x40, 0x7F]).Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheLimits_AreSixtySecondsOfSixteenKilohertzMono()
    {
        Assert.AreEqual(1_920_000, Pcm.MaxBytes);
        Assert.AreEqual(9_600, Pcm.MinBytes);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheBuffer_NeverHoldsMoreThanTheLimit()
    {
        var buffer = new PcmBuffer();
        byte[] chunk = new byte[700_000];

        buffer.Append(chunk);
        buffer.Append(chunk);
        buffer.Append(chunk);
        buffer.Append(chunk);

        Assert.AreEqual(Pcm.MaxBytes, buffer.ToArray().Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRecorderCommandLines_AskForRawSixteenKilohertzMono()
    {
        StringAssert.Contains(ProcessMicrophoneCapture.ParecordArguments(), "--raw --format=s16le --rate=16000 --channels=1");
        StringAssert.Contains(ProcessMicrophoneCapture.ArecordArguments(), "-t raw -f S16_LE -r 16000 -c 1");
        string ffmpeg = ProcessMicrophoneCapture.FfmpegArguments(":default");
        StringAssert.Contains(ffmpeg, "-f avfoundation -i \":default\"");
        StringAssert.Contains(ffmpeg, "-ar 16000 -ac 1 -f s16le -");
        StringAssert.Contains(ffmpeg, "-nostdin");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnLinux_PulseComesBeforeAlsa_AndAMissingProgramIsSkipped()
    {
        List<RecorderCommand> both = MicrophoneCaptureFactory.LinuxCommands(program => "/usr/bin/" + program);
        CollectionAssert.AreEqual(new[] { "/usr/bin/parecord", "/usr/bin/arecord" }, both.Select(c => c.Program).ToArray());

        List<RecorderCommand> alsa = MicrophoneCaptureFactory.LinuxCommands(program => program == "arecord" ? "/bin/arecord" : null);
        CollectionAssert.AreEqual(new[] { "/bin/arecord" }, alsa.Select(c => c.Program).ToArray());

        Assert.AreEqual(0, MicrophoneCaptureFactory.LinuxCommands(_ => null).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnMac_FfmpegTriesTheDefaultInputThenTheFirstOne()
    {
        List<RecorderCommand> commands = MicrophoneCaptureFactory.MacCommands(_ => "/opt/homebrew/bin/ffmpeg");

        Assert.AreEqual(2, commands.Count);
        StringAssert.Contains(commands[0].Arguments, "\":default\"");
        StringAssert.Contains(commands[1].Arguments, "\":0\"");
        Assert.AreEqual(0, MicrophoneCaptureFactory.MacCommands(_ => null).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFailedRecorder_IsDescribedByWhatItSaid()
    {
        StringAssert.Contains(ProcessMicrophoneCapture.Describe("avfoundation: Error: Not authorized to capture audio", isMac: true), "denied");
        string linux = ProcessMicrophoneCapture.Describe("arecord: main:831: audio open error: Permission denied", isMac: false);
        StringAssert.Contains(linux, "Permission denied");
        Assert.IsFalse(linux.Contains("System Settings"), linux);
        StringAssert.Contains(ProcessMicrophoneCapture.Describe(string.Empty), "microphone");
        StringAssert.Contains(ProcessMicrophoneCapture.Describe("\nsome driver exploded\n"), "some driver exploded");
    }

    private static RecorderCommand Shell(string script) => OperatingSystem.IsWindows()
        ? new RecorderCommand("cmd.exe", $"/c \"{script}\"")
        : new RecorderCommand("/bin/sh", $"-c \"{script}\"");

    [Timeout(60_000)]
    [TestMethod]
    public void ARecorderThatExitsAtOnce_FailsWithItsMessage_AndTheNextFormIsTried()
    {
        string failing = OperatingSystem.IsWindows() ? "echo Not authorized 1>&2 & exit 1" : "echo Not authorized >&2; exit 1";
        var capture = new ProcessMicrophoneCapture([Shell(failing)]);

        var exception = Assert.ThrowsExactly<SpeechException>(capture.Start);

        // macOS gets the privacy-settings advice; elsewhere the recorder's own words are shown.
        StringAssert.Contains(exception.Message, OperatingSystem.IsMacOS() ? "denied" : "Not authorized");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WhenTheFirstFormFails_TheSecondIsUsed_AndStopKillsTheRecorder()
    {
        const string failing = "exit 1";
        string running = OperatingSystem.IsWindows() ? "ping -n 60 127.0.0.1 >nul" : "sleep 60";
        var capture = new ProcessMicrophoneCapture([Shell(failing), Shell(running)]);

        capture.Start();
        int id = capture.ProcessId!.Value;
        using (Process alive = Process.GetProcessById(id))
        {
            Assert.IsFalse(alive.HasExited);
        }

        byte[] recorded = capture.Stop();

        Assert.AreEqual(0, recorded.Length);
        Assert.IsTrue(IsGone(id), "the recorder process must not outlive Stop");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Dispose_KillsARunningRecorder()
    {
        string running = OperatingSystem.IsWindows() ? "ping -n 60 127.0.0.1 >nul" : "sleep 60";
        var capture = new ProcessMicrophoneCapture([Shell(running)]);
        capture.Start();
        int id = capture.ProcessId!.Value;

        capture.Dispose();

        Assert.IsTrue(IsGone(id), "the recorder process must not outlive Dispose");
    }

    private static bool IsGone(int id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using Process process = Process.GetProcessById(id);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    [Timeout(60_000)]
    [TestMethod]
    [TestCategory("Windows")]
    public void TheWaveInStructures_HaveTheLayoutWindowsExpects()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("waveIn is Windows only.");
            return;
        }

        Assert.AreEqual(18, System.Runtime.InteropServices.Marshal.SizeOf<WinMmMicrophoneCapture.WaveFormatEx>());
        Assert.AreEqual(IntPtr.Size == 8 ? 48 : 32, System.Runtime.InteropServices.Marshal.SizeOf<WinMmMicrophoneCapture.WaveHeader>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARecorderThatDiesAfterTheStartProbe_IsReportedOnStop_NotSilentlyIgnored()
    {
        // Alive for the 400 ms start-up probe, then gone with a message and a failure code.
        string script = OperatingSystem.IsWindows()
            ? "ping -n 3 127.0.0.1 >nul & echo recorder exploded 1>&2 & exit 1"
            : "sleep 1.2; echo recorder exploded >&2; exit 1";
        var capture = new ProcessMicrophoneCapture([Shell(script)]);
        capture.Start();
        Thread.Sleep(3500);

        var exception = Assert.ThrowsExactly<SpeechException>(capture.Stop);

        StringAssert.Contains(exception.Message, "recorder exploded");
    }
}
