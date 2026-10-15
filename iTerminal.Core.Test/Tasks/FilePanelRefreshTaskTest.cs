using iTerminal.Core.Tasks;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using System.ComponentModel;
using System.Reflection;

namespace iTerminal.Core.Test.Tasks;

[TestClass]
public class FilePanelRefreshTaskTest
{
    private sealed class TestEntry : IEntry
    {
        public TestEntry(string name, Url url)
        {
            Name = name;
            DisplayName = name;
            FullName = url.FullUrl;
            DirectoryName = url.ParentPath;
            Url = url;
        }

        public string FileSize => string.Empty;
        public string Date => string.Empty;
        public string Time => string.Empty;
        public string DisplayName { get; }
        public string Name { get; }
        public string FullName { get; }
        public Url Url { get; }
        public string DirectoryName { get; }
        public string? NumberOfFiles { get; set; }
        public bool Exists => true;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string GetDisplayName(int width) => DisplayName;
        public string GetDisplayFileSize(int width) => FileSize;
        public ConxelString FormatItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
            => new(Name, colorScheme.Foreground, colorScheme.Background);
    }

    [TestMethod]
    public void NameComparer_Instance_SortsCaseInsensitively()
    {
        Type comparerType = typeof(FilePanelRefreshTask).GetNestedType("NameComparer", BindingFlags.NonPublic)!;
        IComparer<IEntry> comparer = (IComparer<IEntry>)comparerType
            .GetField("Instance", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        TestEntry alpha = new("ALPHA.txt", new Url("file:///alpha.txt"));
        TestEntry beta = new("beta.txt", new Url("file:///beta.txt"));

        int result = comparer.Compare(beta, alpha);

        Assert.IsTrue(result > 0);
    }

    /// <summary>
    /// Folders-before-files ordering is a structural property of StreamEntriesAsync, not of
    /// NameComparer (which only sorts within a group). Verify the end-to-end result is correct.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task ExecuteAsync_Result_SortsFoldersBeforeFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "aaa.txt"), "a");
        Directory.CreateDirectory(Path.Combine(dir, "zzz_folder"));

        try
        {
            var cache = new iTerminal.Core.Services.MemoryCacheService();
            var settingsService = new Moq.Mock<iTerminal.Core.Services.ISettingsService>(Moq.MockBehavior.Loose);
            settingsService.Setup(s => s.Get(iTerminal.Core.Services.SettingsService.FOLDER_HISTORY)).Returns((string?)null);
            var feg = new Moq.Mock<iConsole.Services.IFrequentExecutionGuardService>();
            feg.Setup(g => g.Execute(Moq.It.IsAny<Action>())).Callback<Action>(a => a());
            var fs = new Moq.Mock<iTerminal.Core.Services.IFileSystemService>();
            fs.Setup(s => s.ExistsFolder(Moq.It.IsAny<Url>()))
              .Returns<Url>(u => Directory.Exists(u.Path));

            var model = new iTerminal.Core.Model.ContentModels.ContentModelFileSystem(
                "TestPanel", new iConsole.Tasks.TaskManager(), settingsService.Object,
                new iTerminal.Core.Services.FolderHistoryService(settingsService.Object),
                fs.Object, feg.Object, cache, Moq.Mock.Of<Serilog.ILogger>());
            model.ViewSize = new iConsole.Data.Size(80, 20);
            model.SetUrl(new Url(dir));

            var task = new FilePanelRefreshTask(model, cache, Moq.Mock.Of<Serilog.ILogger>());
            await task.ExecuteAsync();

            var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
            // ".." first, then folder, then file
            var dotdotIdx   = names.IndexOf("..");
            var folderIdx   = names.IndexOf("zzz_folder");
            var fileIdx     = names.IndexOf("aaa.txt");

            Assert.IsTrue(dotdotIdx  < folderIdx, $"'..' must come before folder; got [{string.Join(", ", names)}]");
            Assert.IsTrue(folderIdx  < fileIdx,   $"folder must come before file; got [{string.Join(", ", names)}]");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

