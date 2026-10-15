using BenchmarkDotNet.Attributes;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using iTerminal.Core.Tasks;
using iTerminal.Core.Services;
using iConsole.Data;
using Serilog;
using System;
using iConsole.Interfaces;
using iConsole.Display;
using iConsole.Model;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

[MemoryDiagnoser]
public class FileSystemEnumerationBenchmarks
{
    private string _tempRoot = string.Empty;
    private IMemoryCacheService _memoryCache = null!;
    private Serilog.ILogger _loggerSerilog = null!;
    private class DummyContentModel : IContentModel
    {
        public DummyContentModel(Url url)
        {
            Url = url;
            Entries = new ConcurrentObservableCollection<IEntry>();
            FilteredEntries = new List<IEntry>();
        }

        public ConcurrentObservableCollection<IEntry> Entries { get; } = new ConcurrentObservableCollection<IEntry>();
        public string? Filter { get; set; }
        public IList<IEntry> FilteredEntries { get; }
        public long Length => 0;
        public string Name => string.Empty;
        public Url Url { get; }
        public IContentBrowser ContentBrowser { get; set; } = null!;

        public IEntry? GetFocusedEntry() => null;
        public void Refresh(bool forceRefresh = false)
        {
        }

        public void ResetNavigation()
        {
        }

        public void SetUrl(Url url)
        {
        }

        public string GetHeader(int width) => string.Empty;
        public string GetFooter() => string.Empty;
        public ConxelString FormatItemName(IEntry? entry, int width, bool cursor, ColorScheme colorScheme, VisualState visualState) => new(entry?.Name ?? string.Empty, colorScheme.Foreground, colorScheme.Background);
        public void ReadUrl()
        {
        }

        public void Delete(Url url, bool recursive, bool moveToRecycleBin)
        {
        }

        public bool DeleteFile(Url url, bool moveToRecycleBin) => false;

        public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => false;

        public bool ExistsFile(Url url) => false;

        public bool ExistsFolder(Url url) => false;

        public bool IsReadOnly => true;

        public async Task WriteFileAsync(Url url, MemoryStream inputStream) { await Task.CompletedTask; }

        public async Task<IMemoryFile?> ReadFileAsync(Url url) => await Task.FromResult<IMemoryFile?>(null);

        public string GetAvailableDiskSpace() => string.Empty;

        public async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) { await Task.CompletedTask; }

        public async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) { await Task.CompletedTask; }

        public async Task CreateFolderAsync(Url url) { await Task.CompletedTask; }

        public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, System.Threading.CancellationToken cancellationToken) { await Task.CompletedTask; }

        public async Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, System.Threading.CancellationToken cancellationToken) { await Task.CompletedTask; }

        public Task RunTerminalCommandAsync(ITerminalModel terminalModel) => Task.CompletedTask;

        public void RestoreNavigation(string? focusedEntryUrl, long cursorPositionY, long scrollPositionY)
        {
        }

        public Position CursorPosition { get; set; }
        public Position ScrollPosition { get; set; }
        public Size ScrollableSize { get; set; }
        public Size ViewSize { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public void Reset()
        {
        }

        public void ScrollToVisible()
        {
        }

        public void Update()
        {
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "bench_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        // create nested structure
        for (int d = 0; d < 5; d++)
        {
            var dir = Path.Combine(_tempRoot, "dir" + d);
            Directory.CreateDirectory(dir);
            for (int f = 0; f < 200; f++)
            {
                File.WriteAllText(Path.Combine(dir, $"file_{f}.txt"), "content");
            }
        }

        _memoryCache = new MemoryCacheService();
        _loggerSerilog = new LoggerConfiguration().MinimumLevel.Verbose().CreateLogger();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_tempRoot, true);
        }
        catch
        {
        }
    }

    [Benchmark]
    public async Task Benchmark_StreamEntriesAsync()
    {
        var url = new Url(_tempRoot + Path.DirectorySeparatorChar);
        var task = new FilePanelRefreshTask(url, "bench", _memoryCache, _loggerSerilog);
        await task.ExecuteAsync();
    }

    [Benchmark]
    public async Task Benchmark_GetNumberOfFiles()
    {
        var contentModel = new DummyContentModel(new Url(_tempRoot + Path.DirectorySeparatorChar));
        // create an IEntry representing the folder
        var entry = new iTerminal.Core.Model.ContentModels.FileSystemEntry(new Url(_tempRoot + Path.DirectorySeparatorChar));
        var task = new FileInformationRefreshTask(entry, contentModel, _memoryCache, _loggerSerilog, reportBatchSize: 200);
        await task.ExecuteAsync();
    }
}