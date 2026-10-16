using iConsole.Data;
using iConsole.Jobs;
using iTerminal.Core.Services;

namespace iTerminal.Core.Jobs;

public class MemoryDriveSynchronizationJob
    : Job
{
    private ISettingsService _settingsService { get; set; } 
    private IMemoryDriveService _memoryDriveService { get; set; }
    private string SyncPath { get; set; }

    public MemoryDriveSynchronizationJob(ISettingsService settingsService, IMemoryDriveService memoryDriveService)
    {
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _memoryDriveService = memoryDriveService ?? new MemoryDriveService();
        var key = nameof(MemoryDriveSynchronizationJob) + nameof(SyncPath);
        SyncPath = _settingsService.Get(key)!;
        if (string.IsNullOrEmpty(SyncPath))
        {
            SyncPath = Path.Combine(Path.GetTempPath(), "MemoryDrive");
        }
        _settingsService.Set(key, SyncPath);
    }

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        Initialize();
        while (!cancellationToken.IsCancellationRequested)
        {
            WriteIfDirty();
            if (!cancellationToken.IsCancellationRequested)
            {
                // WaitHandle.WaitOne returns immediately once cancellationToken fires, unlike
                // Thread.Sleep, so shutdown (e.g. F10 Quit) isn't stalled waiting out the interval.
                cancellationToken.WaitHandle.WaitOne(1000);
            }
        }
        return Task.CompletedTask;
    }

    private void Initialize()
    {
        try
        {
            if (!Directory.Exists(SyncPath))
            {
                Directory.CreateDirectory(SyncPath);
            }
            var filenames = Directory.EnumerateFiles(SyncPath, "*.*", SearchOption.AllDirectories);
            foreach (var filename in filenames)
            {
                var fileInfo = new FileInfo(filename);
                var content = File.ReadAllBytes(filename);
                var name = Path.GetFileName(filename);
                var url = new Url(Container.MEMORY_DRIVE + name);
                _memoryDriveService.WriteFileAsync(url, new MemoryStream(content));
                _memoryDriveService.Root.Files[name].CreationTime = fileInfo.LastWriteTime;
            }
        }
        catch
        {

        }
    }

    private void WriteIfDirty()
    {
        try
        {
            foreach (var file in _memoryDriveService.Root.Files)
            {
                var fileInfo = new FileInfo(Path.Combine(SyncPath, file.Value.FileName.Replace(Container.MEMORY_DRIVE, string.Empty)));
                var dateMemory = file.Value.CreationTime;
                var dateDisk = fileInfo.LastWriteTime;
                if (!fileInfo.Exists || dateDisk.CompareTo(dateMemory) < 0)
                {
                    File.WriteAllBytes(fileInfo.FullName, file.Value.Content!);
                    fileInfo.LastWriteTime = dateMemory;
                }
            }
            var files = Directory.EnumerateFiles(SyncPath);
            foreach (var file in files)
            {
                if (!_memoryDriveService.Root.Files.Any(f => f.Key == Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
            }

        }
        catch
        {
        }
    }

    public override void OnAfterStop()
    {
        WriteIfDirty();
    }
}
