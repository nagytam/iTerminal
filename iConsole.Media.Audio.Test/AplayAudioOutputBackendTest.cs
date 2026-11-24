using System.Diagnostics;
using iConsole.Media.Audio;
using iConsole.Media.Audio.Sinks;

namespace iConsole.Media.Audio.Test;

/// <summary>
/// Runs the real ALSA backend against a fake <c>aplay</c> placed first on PATH. Linux only, since the
/// backend itself reports nothing elsewhere.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AplayAudioOutputBackendTest
{
    private string _binDirectory = string.Empty;
    private string _originalPath = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _binDirectory = Path.Combine(Path.GetTempPath(), "AplayFake_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_binDirectory);
        _originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        try
        {
            Directory.Delete(_binDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [TestMethod]
    [Timeout(120_000)]
    public void GetDevices_ParsesTheListWhenAplayAnswersAtOnce()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The ALSA backend only runs on Linux.");
        }

        WriteFakeAplay(
            "if [ \"$1\" = \"--version\" ]; then echo aplay; exit 0; fi\n" +
            "printf 'plughw:0,0\\n    Built-in Audio\\n'\n");
        UsePath();

        var backend = new AplayAudioOutputBackend();

        Assert.IsTrue(backend.IsAvailable());
        var devices = backend.GetDevices();
        Assert.IsTrue(devices.Any(d => d.Id == "plughw:0,0"), "The plughw entry must be listed.");
    }

    [TestMethod]
    [Timeout(120_000)]
    public void GetDevices_GivesUpOnAHungAplayInsteadOfWaitingForIt()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The ALSA backend only runs on Linux.");
        }

        // Without the timeout on the pipe reads this call waits the whole 60 seconds.
        WriteFakeAplay("sleep 60\n");
        UsePath();

        var backend = new AplayAudioOutputBackend();
        var watch = Stopwatch.StartNew();
        var devices = backend.GetDevices();
        watch.Stop();

        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(30), $"Took {watch.Elapsed}, the 5 s budget was not applied.");
        Assert.AreEqual(1, devices.Count, "Only the default entry is offered when aplay gives no answer.");
    }

    private void WriteFakeAplay(string body)
    {
        var path = Path.Combine(_binDirectory, "aplay");
        File.WriteAllText(path, "#!/bin/sh\n" + body);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private void UsePath() =>
        Environment.SetEnvironmentVariable("PATH", _binDirectory + Path.PathSeparator + _originalPath);
}
