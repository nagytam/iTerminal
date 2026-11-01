using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;
using System.Buffers.Binary;
using System.Diagnostics;

namespace iTerminal.Core.Test.Services.Music;

/// <summary>
/// Drives the real engine — pump thread, decoder factory and all — against a fake sink. Assertions
/// poll with a deadline because playback is genuinely asynchronous; that is the same approach
/// <c>FilePanelIntegrationTest</c> takes for task-driven state.
/// </summary>
[TestClass]
public class MusicPlayerServiceTest
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MusicPlayerTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    private static bool WaitFor(Func<bool> condition, string because)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(15);
        }

        Assert.Fail($"Timed out waiting for: {because}");
        return false;
    }

    private string WriteTone(string name, TimeSpan duration, int sampleRate = 8000) =>
        TestAudioFiles.WriteSilence(_tempRoot, name, duration, sampleRate);

    /// <summary>
    /// A track long enough that it cannot finish while a test is still looking at it. Fed at 20 ms
    /// per 100 ms chunk, a minute of audio is ~12 s of wall clock — wide enough that transient
    /// states like Playing and Paused stay observable even when the suite runs classes in parallel.
    /// </summary>
    private string WriteLongTone(string name) => WriteTone(name, TimeSpan.FromMinutes(1));

    [Timeout(60_000)]
    [TestMethod]
    public void PlayIndex_OpensTheSinkWithTheDecoderFormatAndFeedsIt()
    {
        var path = WriteTone("a.wav", TimeSpan.FromSeconds(0.5));
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(path));

        player.PlayIndex(0);

        // 0.5 s of 8 kHz stereo 16-bit is 4000 frames of 4 bytes. Waiting on the byte count is the
        // only stable signal here: the sink object exists from the moment it is created, before any
        // audio has been written, and the engine is back to Stopped once the track ends.
        const int expectedBytes = 16000;
        WaitFor(() => deviceService.LastSink is { } written && written.WrittenByteCount >= expectedBytes,
            "the whole track to be written to the device");
        WaitFor(() => player.State == PlaybackState.Stopped, "playback to finish");

        var sink = deviceService.LastSink!;
        Assert.AreEqual(8000, sink.OpenedFormat.SampleRate);
        Assert.AreEqual(2, sink.OpenedFormat.Channels);
        Assert.AreEqual(expectedBytes, sink.WrittenByteCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PlayIndex_ReportsDurationFromTheDecoder()
    {
        var path = WriteTone("a.wav", TimeSpan.FromSeconds(2));
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(path));

        player.PlayIndex(0);
        // Duration is latched when the decoder opens and survives the track ending, so waiting on
        // it is stable regardless of how fast playback runs.
        WaitFor(() => player.Duration > TimeSpan.Zero, "the duration to be reported");

        Assert.AreEqual(2.0, player.Duration.TotalSeconds, 0.05);
        player.Stop();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PlayIndex_BackfillsTheDurationOntoThePlaylistRow()
    {
        var path = WriteTone("a.wav", TimeSpan.FromSeconds(1));
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(path));

        Assert.AreEqual(TimeSpan.Zero, player.Playlist[0]!.Duration, "Duration is unknown before the first play");

        player.PlayIndex(0);
        WaitFor(() => player.Playlist[0]!.Duration > TimeSpan.Zero, "the duration to be written back");

        Assert.AreEqual(1.0, player.Playlist[0]!.Duration.TotalSeconds, 0.05);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NaturalEnd_WithRepeatOff_StopsAfterTheLastTrack()
    {
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteTone("a.wav", TimeSpan.FromSeconds(0.2))));

        player.PlayIndex(0);

        // The engine starts Stopped, so waiting on the state alone would pass before anything ran.
        WaitFor(() => deviceService.CreatedSinks.Count == 1 && player.State == PlaybackState.Stopped,
            "playback to stop at the end of the playlist");

        Thread.Sleep(200);
        Assert.AreEqual(1, deviceService.CreatedSinks.Count, "No further track should have been started");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NaturalEnd_AdvancesToTheNextTrackOnItsOwn()
    {
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteTone("a.wav", TimeSpan.FromSeconds(0.2))));
        player.Playlist.Add(new MusicTrack(WriteTone("b.wav", TimeSpan.FromSeconds(0.2))));

        player.PlayIndex(0);

        // The pump, not the caller, is responsible for moving on when a track ends. Assert on the
        // sinks rather than on CurrentIndex: with a non-blocking fake sink these short tracks are
        // consumed in microseconds, so the intermediate cursor value is not reliably observable.
        WaitFor(() => deviceService.CreatedSinks.Count == 2, "the engine to start the second track on its own");
        WaitFor(() => player.State == PlaybackState.Stopped, "the playlist to finish");

        Assert.AreEqual(
            player.Playlist[1]!.Path,
            deviceService.CreatedSinks[1].WrittenByteCount > 0 ? player.Playlist[1]!.Path : string.Empty,
            "The second sink must actually have received audio");
    }

    /// <summary>
    /// Playback is the only work in this app with a real-time deadline — the device drains its buffer
    /// whether or not the pump gets scheduled — so the pump must not compete with rendering and
    /// background tasks on equal terms.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void PumpThread_RunsAboveTheRestOfTheApp()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));

        player.PlayIndex(0);
        WaitFor(() => player.State == PlaybackState.Playing, "playback to start");

        var pump = player.PumpThreadForTest;

        Assert.IsNotNull(pump);
        Assert.IsTrue(
            pump!.Priority > ThreadPriority.Normal,
            $"The audio pump must outrank ordinary work; it is {pump.Priority}");
        Assert.IsTrue(pump.IsBackground, "…but must not keep the process alive on its own");

        player.Stop();
    }

    /// <summary>
    /// The advance has to be announced while the new track is the cued one. It is not enough that
    /// <c>CurrentIndex</c> moves: the panel only rebuilds its rows on <c>StateChanged</c>, and the
    /// state itself does not change from one track to the next — both are "playing" — so an advance
    /// that relies on <c>SetState</c> to notify announces nothing at all.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void NaturalEnd_AnnouncesTheTrackItMovedTo()
    {
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteTone("a.wav", TimeSpan.FromSeconds(0.2))));
        player.Playlist.Add(new MusicTrack(WriteTone("b.wav", TimeSpan.FromSeconds(0.2))));

        List<int> announced = [];
        player.StateChanged += (_, _) =>
        {
            lock (announced)
            {
                announced.Add(player.Playlist.CurrentIndex);
            }
        };

        player.PlayIndex(0);

        WaitFor(() => deviceService.CreatedSinks.Count == 2, "the engine to start the second track on its own");
        WaitFor(() => player.State == PlaybackState.Stopped, "the playlist to finish");

        lock (announced)
        {
            CollectionAssert.Contains(
                announced,
                1,
                "Some notification has to arrive while the second track is the current one, or the panel"
                + " keeps its highlight on the track that finished");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NaturalEnd_WithRepeatOne_ReplaysTheSameTrack()
    {
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService) { RepeatMode = RepeatMode.One };
        player.Playlist.Add(new MusicTrack(WriteTone("a.wav", TimeSpan.FromSeconds(0.2))));
        player.Playlist.Add(new MusicTrack(WriteTone("b.wav", TimeSpan.FromSeconds(0.2))));

        player.PlayIndex(0);

        WaitFor(() => deviceService.CreatedSinks.Count >= 3, "the track to repeat several times");
        Assert.AreEqual(0, player.Playlist.CurrentIndex, "Repeat-one must not move off the track");
        player.Stop();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Pause_HaltsTheFlowAndResumeContinuesIt()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));

        player.PlayIndex(0);
        WaitFor(() => player.State == PlaybackState.Playing, "playback to start");

        player.Pause();
        WaitFor(() => player.State == PlaybackState.Paused, "the engine to report paused");

        var sink = deviceService.LastSink!;
        Assert.IsTrue(sink.IsPaused, "The sink must be told to pause, not just starved of data");

        var bytesAtPause = sink.WrittenByteCount;
        Thread.Sleep(150);
        Assert.AreEqual(bytesAtPause, sink.WrittenByteCount, "No audio may be written while paused");

        player.Resume();
        WaitFor(() => player.State == PlaybackState.Playing, "playback to resume");
        WaitFor(() => sink.WrittenByteCount > bytesAtPause, "audio to flow again after resuming");

        player.Stop();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TogglePlayPause_FromStopped_StartsTheCuedTrack()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));

        player.TogglePlayPause();

        WaitFor(() => player.State == PlaybackState.Playing, "the first track to start");
        player.Stop();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Stop_EndsPlaybackAndReleasesTheSink()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));

        player.PlayIndex(0);
        WaitFor(() => player.State == PlaybackState.Playing, "playback to start");

        player.Stop();

        WaitFor(() => player.State == PlaybackState.Stopped, "playback to stop");
        WaitFor(() => deviceService.LastSink!.IsDisposed, "the sink to be disposed");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Next_MovesToTheFollowingTrackImmediately()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));
        player.Playlist.Add(new MusicTrack(WriteLongTone("b.wav")));

        player.PlayIndex(0);
        WaitFor(() => player.State == PlaybackState.Playing, "the first track to start");

        player.Next();

        WaitFor(() => player.Playlist.CurrentIndex == 1, "the second track to become current");
        player.Stop();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Previous_PartwayThroughATrack_RestartsItInsteadOfStepingBack()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));
        player.Playlist.Add(new MusicTrack(WriteLongTone("b.wav")));

        player.PlayIndex(1);
        WaitFor(() => player.State == PlaybackState.Playing, "playback to start");

        // Seek rather than waiting for playback to reach the 3 s restart threshold on its own:
        // under parallel test execution, throughput is not something this assertion should depend on.
        player.Seek(TimeSpan.FromSeconds(10));
        WaitFor(() => player.Position >= TimeSpan.FromSeconds(9), "the position to move well past the restart threshold");

        player.Previous();

        WaitFor(() => player.Position < TimeSpan.FromSeconds(3), "the track to restart from the beginning");
        Assert.AreEqual(1, player.Playlist.CurrentIndex, "Restarting must not change the track");
        player.Stop();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Seek_MovesThePositionAndDropsBufferedAudio()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));

        player.PlayIndex(0);
        WaitFor(() => player.State == PlaybackState.Playing, "playback to start");

        player.Seek(TimeSpan.FromSeconds(5));

        WaitFor(() => player.Position >= TimeSpan.FromSeconds(5), "the position to jump forward");
        WaitFor(() => deviceService.LastSink!.FlushCount > 0, "the sink to be flushed on seek");
        player.Stop();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetDevice_ReopensTheSinkOnTheNewDeviceAndKeepsPlaying()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));

        player.PlayIndex(0);
        WaitFor(() => player.State == PlaybackState.Playing, "playback to start");
        WaitFor(() => player.Position > TimeSpan.FromSeconds(0.3), "playback to make progress");

        player.SetDevice("2");

        WaitFor(() => deviceService.CreatedSinks.Count >= 2, "a second sink to be opened for the new device");
        WaitFor(() => player.State == PlaybackState.Playing, "playback to continue on the new device");

        Assert.AreEqual("2", deviceService.LastSink!.OpenedDeviceId);
        Assert.AreEqual("2", player.DeviceId);
        Assert.IsTrue(player.Position > TimeSpan.Zero, "Switching device must resume, not restart");
        player.Stop();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetDevice_WhileStopped_JustRemembersTheChoice()
    {
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService);

        player.SetDevice("1");

        Assert.AreEqual("1", player.DeviceId);
        Assert.AreEqual(0, deviceService.CreatedSinks.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MissingFile_ReportsAnErrorAndSkipsToTheNextTrack()
    {
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(Path.Combine(_tempRoot, "gone.wav")));
        player.Playlist.Add(new MusicTrack(WriteTone("b.wav", TimeSpan.FromSeconds(0.2))));

        player.PlayIndex(0);

        WaitFor(() => player.StatusIsError, "an error to be reported for the missing file");
        Assert.IsTrue(player.StatusMessage.Contains("gone.wav", StringComparison.OrdinalIgnoreCase));

        // The playable entry after it must still get its turn.
        WaitFor(() => deviceService.CreatedSinks.Count == 1 && deviceService.CreatedSinks[0].WrittenByteCount > 0,
            "the engine to skip the unplayable entry and play the next one");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AllTracksUnplayable_StopsInsteadOfLoopingForever()
    {
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService) { RepeatMode = RepeatMode.All };
        player.Playlist.Add(new MusicTrack(Path.Combine(_tempRoot, "gone1.wav")));
        player.Playlist.Add(new MusicTrack(Path.Combine(_tempRoot, "gone2.wav")));

        player.PlayIndex(0);

        WaitFor(() => player.StatusIsError, "the engine to report the failures");
        // Repeat-all plus nothing playable is the case that could spin forever; it must settle.
        WaitFor(() => player.State == PlaybackState.Stopped, "the engine to give up on an unplayable playlist");

        Thread.Sleep(300);
        Assert.AreEqual(PlaybackState.Stopped, player.State, "The engine must stay stopped, not keep retrying");
        Assert.AreEqual(0, deviceService.CreatedSinks.Count, "No sink should ever have been opened");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoAudioBackend_ReportsItRatherThanThrowing()
    {
        var deviceService = new FakeAudioDeviceService(hasOutput: false);
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteTone("a.wav", TimeSpan.FromSeconds(0.2))));

        player.PlayIndex(0);

        WaitFor(() => player.StatusIsError && player.StatusMessage.Contains("backend", StringComparison.OrdinalIgnoreCase),
            "the engine to report that there is no audio output");
        Assert.AreEqual(PlaybackState.Stopped, player.State);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PlayIndex_OutOfRange_Stops()
    {
        var deviceService = new FakeAudioDeviceService();
        using var player = new MusicPlayerService(deviceService);

        player.PlayIndex(5);

        Assert.AreEqual(PlaybackState.Stopped, player.State);
        Assert.AreEqual(0, deviceService.CreatedSinks.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Volume_IsClampedToZeroHundred()
    {
        using var player = new MusicPlayerService(new FakeAudioDeviceService());

        player.Volume = 250;
        Assert.AreEqual(100, player.Volume);

        player.Volume = -40;
        Assert.AreEqual(0, player.Volume);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ApplyVolume_AtFullVolume_LeavesSamplesUntouched()
    {
        var pcm = TestAudioFiles.Pcm16(1000, -1000, short.MaxValue);
        var original = pcm.ToArray();

        MusicPlayerService.ApplyVolume(pcm, 100);

        CollectionAssert.AreEqual(original, pcm);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ApplyVolume_AtZero_Silences()
    {
        var pcm = TestAudioFiles.Pcm16(1000, -1000, short.MaxValue);

        MusicPlayerService.ApplyVolume(pcm, 0);

        CollectionAssert.AreEqual(new byte[pcm.Length], pcm);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ApplyVolume_UsesAQuadraticCurve()
    {
        var pcm = TestAudioFiles.Pcm16(10000);

        MusicPlayerService.ApplyVolume(pcm, 50);

        // 0.5^2 = 0.25 — a linear curve would leave 5000 here and sound far too loud at half.
        Assert.AreEqual(2500, BinaryPrimitives.ReadInt16LittleEndian(pcm), 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Dispose_WhilePlaying_StopsThePumpThread()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));

        player.PlayIndex(0);
        WaitFor(() => player.State == PlaybackState.Playing, "playback to start");

        var stopwatch = Stopwatch.StartNew();
        player.Dispose();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3), "Dispose must not block on a long track");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TransportCommands_ReturnWellInsideTheKeyHandlerBudget()
    {
        var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
        using var player = new MusicPlayerService(deviceService);
        player.Playlist.Add(new MusicTrack(WriteLongTone("a.wav")));
        player.Playlist.Add(new MusicTrack(WriteLongTone("b.wav")));

        player.PlayIndex(0);
        WaitFor(() => player.State == PlaybackState.Playing, "playback to start");

        // Key handlers must return within 200 ms; the engine defers all real work to its pump.
        foreach (var (name, action) in new (string, Action)[]
        {
            ("PlayIndex", () => player.PlayIndex(1)),
            ("Pause", player.Pause),
            ("Resume", player.Resume),
            ("Seek", () => player.Seek(TimeSpan.FromSeconds(2))),
            ("Next", player.Next),
            ("Previous", player.Previous),
            ("Stop", player.Stop),
        })
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            Assert.IsTrue(
                stopwatch.ElapsedMilliseconds < 200,
                $"{name} took {stopwatch.ElapsedMilliseconds} ms, over the 200 ms key-handler budget");
        }
    }

    /// <summary>
    /// Starting a track stops whatever sink was playing so the switch is immediate. It used to read
    /// the sink to stop *after* posting the request — by when the pump could already have opened the
    /// new track's sink, which was then stopped: playback ended after one chunk with "The audio
    /// device stopped accepting data". A fast pump start wins that race, so many players are started
    /// at once to make it show.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void StartingATrackNeverStopsTheSinkOpenedForIt()
    {
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var deviceService = new FakeAudioDeviceService { WriteDelay = TimeSpan.FromMilliseconds(20) };
            using var player = new MusicPlayerService(deviceService);
            player.Playlist.Add(new MusicTrack(WriteTone($"race{i}.wav", TimeSpan.FromSeconds(20))));

            player.PlayIndex(0);

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < Deadline && player.Position <= TimeSpan.FromSeconds(0.3))
            {
                Thread.Sleep(15);
            }
            if (player.Position <= TimeSpan.FromSeconds(0.3))
            {
                failures.Add($"#{i}: {player.State}, '{player.StatusMessage}'");
            }
            player.Stop();
        });

        Assert.IsTrue(failures.IsEmpty, string.Join(Environment.NewLine, failures));
    }
}