using iConsole.Jobs;
using iTerminal.Core.Data;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class MusicLibraryIndexJobTest
{
    private string _tempRoot = string.Empty;
    private string _musicFolder = string.Empty;
    private SettingsService _settingsService = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MusicIndexJobTest_" + Guid.NewGuid().ToString("N")[..8]);
        _musicFolder = Path.Combine(_tempRoot, "Music");
        Directory.CreateDirectory(_musicFolder);

        _settingsService = new SettingsService();
        _settingsService.Set(Settings.MusicFolder, _musicFolder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    private static MusicLibraryIndexJob CreateJob(IMusicLibraryService library) =>
        new(library)
        {
            StartupDelay = TimeSpan.Zero,
            FolderPollInterval = TimeSpan.FromMilliseconds(20),
            RescanInterval = TimeSpan.FromMinutes(15),
        };

    private static async Task WaitForAsync(Func<bool> condition, string because, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for: {because}");
    }

    [TestMethod]
    public void Priority_IsBackground()
    {
        Assert.AreEqual(JobPriority.Background, CreateJob(new MusicLibraryService(null, _settingsService)).Priority);
    }

    [TestMethod]
    public async Task Run_IndexesTheConfiguredFolder()
    {
        TestAudioFiles.WriteId3V2File(_musicFolder, "song.mp3", "Song", "Artist", "Album");

        var library = new MusicLibraryService(null, _settingsService);
        var job = CreateJob(library);

        using var cancellation = new CancellationTokenSource();
        var run = job.ExecuteAsync(cancellation.Token);

        await WaitForAsync(() => library.TrackCount == 1, "the job to index the music folder");

        await cancellation.CancelAsync();
        await run;
    }

    [TestMethod]
    public async Task Run_ReindexesWhenTheMusicFolderSettingChanges()
    {
        var other = Path.Combine(_tempRoot, "Other");
        Directory.CreateDirectory(other);
        TestAudioFiles.WriteId3V2File(_musicFolder, "one.mp3", "One", "Artist", "Album");
        TestAudioFiles.WriteId3V2File(other, "two.mp3", "Two", "Artist", "Album");
        TestAudioFiles.WriteId3V2File(other, "three.mp3", "Three", "Artist", "Album");

        var library = new MusicLibraryService(null, _settingsService);
        var job = CreateJob(library);

        using var cancellation = new CancellationTokenSource();
        var run = job.ExecuteAsync(cancellation.Token);

        await WaitForAsync(() => library.TrackCount == 1, "the first folder to be indexed");

        // The job polls the setting rather than being told, so pointing it elsewhere is enough.
        _settingsService.Set(Settings.MusicFolder, other);

        await WaitForAsync(() => library.TrackCount == 2, "the new folder to replace the old index");

        await cancellation.CancelAsync();
        await run;
    }

    /// <summary>
    /// The cached table has to be read by this job, not by whichever key handler opens the catalog
    /// first — a lazy load on the panel's thread is a full SQLite read inside the 200 ms budget.
    /// </summary>
    [TestMethod]
    public async Task Run_LoadsTheCachedIndexBeforeItEvenWaits()
    {
        var library = new Test.Services.Music.FakeMusicLibraryService();
        var job = new MusicLibraryIndexJob(library)
        {
            // Long enough that the test would time out if the preload waited for it.
            StartupDelay = TimeSpan.FromMinutes(5),
            FolderPollInterval = TimeSpan.FromMilliseconds(20),
        };

        using var cancellation = new CancellationTokenSource();
        var run = job.ExecuteAsync(cancellation.Token);

        await WaitForAsync(() => library.PreloadCount > 0, "the job to warm the cached index");
        Assert.AreEqual(0, library.RefreshCount, "…without having started a scan yet");

        await cancellation.CancelAsync();
        await run;
    }

    [TestMethod]
    public async Task Run_StopsWhenCancelled()
    {
        var library = new MusicLibraryService(null, _settingsService);
        var job = CreateJob(library);

        using var cancellation = new CancellationTokenSource();
        var run = job.ExecuteAsync(cancellation.Token);
        await WaitForAsync(() => library.LastIndexedUtc is not null, "the first pass to finish");

        await cancellation.CancelAsync();

        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(run.IsCompleted);
    }
}
