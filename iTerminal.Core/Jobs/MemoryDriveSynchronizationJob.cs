using System.Diagnostics;
﻿using iConsole.Data;
using iConsole.Jobs;
using iTerminal.Core.Services;

namespace iTerminal.Core.Jobs;

public class MemoryDriveSynchronizationJob
    : Job
{
    private ISettingsService _settingsService { get; set; } 
    private IMemoryDriveService _memoryDriveService { get; set; }
    private string SyncPath { get; set; } = string.Empty;
    private static readonly string SyncPathKey = nameof(MemoryDriveSynchronizationJob) + nameof(SyncPath);

    public MemoryDriveSynchronizationJob(ISettingsService settingsService, IMemoryDriveService memoryDriveService)
    {
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _memoryDriveService = memoryDriveService ?? new MemoryDriveService();
        ResolveSyncPath();
    }

    /// <summary>
    /// The folder the memory drive mirrors itself to. Asked again when the job starts: the job is built
    /// with the container, before the settings are read from the database, so a value stored there (or
    /// seeded by a sandbox) was missed and the shared temp folder used instead.
    /// </summary>
    private void ResolveSyncPath()
    {
        SyncPath = _settingsService.Get(SyncPathKey)!;
        if (string.IsNullOrEmpty(SyncPath))
        {
            SyncPath = Path.Combine(Path.GetTempPath(), "MemoryDrive");
        }
        _settingsService.Set(SyncPathKey, SyncPath);
    }

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        ResolveSyncPath();
        Initialize();
        _memoryDriveService.NotifyMirrorLoaded();
        while (!cancellationToken.IsCancellationRequested)
        {
            // The settings may finish loading after this job starts, so the folder can change under
            // it: the one just found is then the one to mirror, and what it holds is loaded.
            var previousSyncPath = SyncPath;
            ResolveSyncPath();
            if (SyncPath != previousSyncPath)
            {
                Initialize();
                _memoryDriveService.NotifyMirrorLoaded();
            }

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
            // Only the folder itself: the memory drive is flat, and a file in a subfolder would be loaded under its bare
            // name and collide with a root file of the same name.
            var filenames = Directory.EnumerateFiles(SyncPath, "*.*", SearchOption.TopDirectoryOnly);
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
        catch (Exception exception)
        {
            Debug.WriteLine($"{nameof(MemoryDriveSynchronizationJob)}.{nameof(Initialize)} failed: {exception}");
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
        catch (Exception exception)
        {
            // Kept running: the next pass tries again. Traced, so a folder that cannot be written is not a silent loss.
            Debug.WriteLine($"{nameof(MemoryDriveSynchronizationJob)}.{nameof(WriteIfDirty)} failed: {exception}");
        }
    }

    public override void OnAfterStop()
    {
        WriteIfDirty();
    }
}
