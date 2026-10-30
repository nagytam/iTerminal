using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs.Torrents;
using iTerminal.Core.Services.Torrents;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Torrents;

// Some tests paste from the ambient clipboard, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class TorrentManagerDialogTest
{
    internal sealed class FakeTorrents : ITorrentService
    {
        public List<TorrentItem> Items { get; } = [];
        public List<string> Calls { get; } = [];
        public string DefaultDownloadDirectory => "/downloads";
        public IReadOnlyList<TorrentItem> Snapshot() => [.. Items];

        public Task<string> AddAsync(string source, string? saveDirectory = null, CancellationToken cancellationToken = default)
        {
            Calls.Add($"add {source} -> {saveDirectory}");
            Items.Add(Item("added", TorrentStatus.Downloading));
            return Task.FromResult("added");
        }

        public Task RestoreAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("restore");
            return Task.CompletedTask;
        }

        public Task PauseAsync(string id)
        {
            Calls.Add("pause " + id);
            Replace(id, TorrentStatus.Paused);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(string id)
        {
            Calls.Add("resume " + id);
            Replace(id, TorrentStatus.Downloading);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string id, bool deleteData)
        {
            Calls.Add($"remove {id} {deleteData}");
            Items.RemoveAll(i => i.Id == id);
            return Task.CompletedTask;
        }

        public Task ShutdownAsync() => Task.CompletedTask;

        private void Replace(string id, TorrentStatus status)
        {
            int index = Items.FindIndex(i => i.Id == id);
            Items[index] = Items[index] with { Status = status };
        }
    }

    internal static TorrentItem Item(string id, TorrentStatus status = TorrentStatus.Downloading, double progress = 42.5) =>
        new(id, "Ubuntu " + id, status, progress, 3L * 1024 * 1024 * 1024, 2 * 1024 * 1024, 1024, 12, "/downloads", null);

    private static (TorrentManagerDialog Dialog, FakeTorrents Torrents, Mock<IContentModelService> Panels) Create(params TorrentItem[] items)
    {
        FakeTorrents torrents = new();
        torrents.Items.AddRange(items);
        Mock<IContentModelService> panels = new();
        panels.Setup(p => p.SetUrlAsync(It.IsAny<Url>())).Returns(Task.CompletedTask);
        return (new TorrentManagerDialog(torrents, panels.Object, TestTheme()), torrents, panels);
    }

    private static async Task TypeAsync(TorrentManagerDialog dialog, string text)
    {
        foreach (char c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    [TestMethod]
    public async Task AnEmptyManagerSaysHowToAdd()
    {
        var (dialog, torrents, _) = Create();

        await ShowAsync(dialog);
        await dialog.PendingOperation;

        AssertScreenContains(Render(dialog), "No torrents yet. A adds a magnet link");
        CollectionAssert.Contains(torrents.Calls, "restore");
    }

    [TestMethod]
    public async Task TheListShowsNameStateProgressAndSpeed()
    {
        var (dialog, _, _) = Create(Item("one"));

        await ShowAsync(dialog);

        var screen = Render(dialog);
        AssertScreenContains(screen, "Ubuntu one");
        AssertScreenContains(screen, "Down");
        AssertScreenContains(screen, "42.5%");
        AssertScreenContains(screen, "3.0 GB");
        AssertScreenContains(screen, "2.0 MB/s");
    }

    [TestMethod]
    public async Task AMagnetLinkIsAddedWithTheDefaultFolder()
    {
        var (dialog, torrents, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('a'));
        await TypeAsync(dialog, "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await dialog.PendingOperation;

        CollectionAssert.Contains(torrents.Calls, "add magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567 -> /downloads");
        Assert.AreEqual("List", dialog.ModeForTest);
        Assert.AreEqual(1, dialog.ListCountForTest);
    }

    [TestMethod]
    public async Task AnInvalidSourceIsRefusedOnTheForm()
    {
        var (dialog, torrents, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('a'));
        await TypeAsync(dialog, "magnet:nonsense");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual("Add", dialog.ModeForTest);
        StringAssert.Contains(dialog.StatusForTest, "not a magnet link");
        Assert.IsFalse(torrents.Calls.Any(c => c.StartsWith("add ")));
    }

    [TestMethod]
    public async Task EscLeavesTheForm()
    {
        var (dialog, _, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('a'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.AreEqual("List", dialog.ModeForTest);
    }

    [TestMethod]
    public async Task SpacePausesThenResumes()
    {
        var (dialog, torrents, _) = Create(Item("one"));
        await ShowAsync(dialog);
        await dialog.PendingOperation; // the restore started on opening

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        await dialog.PendingOperation;
        dialog.RefreshForTest();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        await dialog.PendingOperation;

        CollectionAssert.AreEqual(new[] { "restore", "pause one", "resume one" }, torrents.Calls);
    }

    [TestMethod]
    public async Task RemovingAsksFirstAndYKeepsTheFiles()
    {
        var (dialog, torrents, _) = Create(Item("one"));
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('d'));
        Assert.IsFalse(torrents.Calls.Any(c => c.StartsWith("remove")));
        await dialog.HandleKeyEventAsync(Char('y'));
        await dialog.PendingOperation;

        CollectionAssert.Contains(torrents.Calls, "remove one False");
        Assert.AreEqual(0, dialog.ListCountForTest);
    }

    [TestMethod]
    public async Task WRemovesTheFilesToo()
    {
        var (dialog, torrents, _) = Create(Item("one"));
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        await dialog.HandleKeyEventAsync(Char('w'));
        await dialog.PendingOperation;

        CollectionAssert.Contains(torrents.Calls, "remove one True");
    }

    [TestMethod]
    public async Task AnyOtherKeyCancelsTheRemoval()
    {
        var (dialog, torrents, _) = Create(Item("one"));
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('d'));
        await dialog.HandleKeyEventAsync(Char('n'));

        Assert.IsFalse(torrents.Calls.Any(c => c.StartsWith("remove")));
        Assert.AreEqual(1, dialog.ListCountForTest);
    }

    [TestMethod]
    public async Task TheArrowsMoveTheHighlight()
    {
        var (dialog, _, _) = Create(Item("one"), Item("two"));
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.AreEqual(1, dialog.CursorForTest);
    }

    [TestMethod]
    public async Task OOpensTheSaveFolderInThePanel()
    {
        var (dialog, _, panels) = Create(Item("one"));
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('o'));
        await dialog.PendingOperation;

        panels.Verify(p => p.SetUrlAsync(It.IsAny<Url>()), Times.Once);
    }

    [TestMethod]
    public void SizesAndRatesAreReadable()
    {
        Assert.AreEqual("512 B", TorrentManagerDialog.FormatSize(512));
        Assert.AreEqual("1.5 KB", TorrentManagerDialog.FormatSize(1536));
        Assert.AreEqual("-", TorrentManagerDialog.FormatRate(0));
        Assert.AreEqual("[#####-----]", TorrentManagerDialog.ProgressBar(50, 12));
    }
}
