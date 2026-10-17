using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Tasks.Test;

[TestClass]
public class TransferSpaceGuardTest
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class TinyDisk(long free, Func<Url, string?> volume) : FileSystemService
    {
        public override long? GetFreeBytes(Url url) => free;

        public override string? GetVolumeId(Url url) => volume(url);
    }

    /// <summary>Answers the prompts in order; true is the first button.</summary>
    private sealed class ScriptedPrompts(params bool[] answers) : IErrorPromptService
    {
        private int _next;

        public List<string> Titles { get; } = [];

        public Task<bool> ConfirmContinueAsync(string title, string message, CancellationToken cancellationToken) =>
            ConfirmAsync(title, message, "Continue", "Cancel", cancellationToken);

        public Task<bool> ConfirmAsync(string title, string message, string acceptLabel, string declineLabel, CancellationToken cancellationToken)
        {
            Titles.Add(title);
            return Task.FromResult(answers[_next++]);
        }
    }

    private (string Source, string Target) Files(string content = "0123456789")
    {
        var source = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(source, content);
        return (source, Path.Combine(_tempDir, "target.txt"));
    }

    [TestMethod]
    public async Task Copy_WithEnoughSpace_AsksNothing()
    {
        var (source, target) = Files();
        var prompts = new ScriptedPrompts();
        var disk = new TinyDisk(1000, _ => "C");

        await new ContentModelCopyFileTask(disk, disk, new Url(source), new Url(target), false, prompts).ExecuteAsync();

        Assert.AreEqual(0, prompts.Titles.Count);
        Assert.IsTrue(File.Exists(target));
    }

    [TestMethod]
    public async Task Copy_WithoutEnoughSpace_WarnsAndGoesOnWhenTheUserSaysSo()
    {
        var (source, target) = Files();
        var prompts = new ScriptedPrompts(false);
        var disk = new TinyDisk(5, _ => "C");

        await new ContentModelCopyFileTask(disk, disk, new Url(source), new Url(target), false, prompts).ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "Not enough free space" }, prompts.Titles);
        Assert.IsTrue(File.Exists(target));
    }

    [TestMethod]
    public async Task Copy_PausedThenResumed_AsksAgainAndCopiesOnceTheUserGoesOn()
    {
        var (source, target) = Files();
        var prompts = new ScriptedPrompts(true, true, false);
        var disk = new TinyDisk(5, _ => "C");

        await new ContentModelCopyFileTask(disk, disk, new Url(source), new Url(target), false, prompts).ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "Not enough free space", "Copy paused", "Not enough free space" }, prompts.Titles);
        Assert.IsTrue(File.Exists(target));
    }

    [TestMethod]
    public async Task Copy_PausedThenCancelled_CopiesNothing()
    {
        var (source, target) = Files();
        var prompts = new ScriptedPrompts(true, false);
        var disk = new TinyDisk(5, _ => "C");
        var task = new ContentModelCopyFileTask(disk, disk, new Url(source), new Url(target), false, prompts);

        await task.ExecuteAsync();

        Assert.IsFalse(File.Exists(target));
        Assert.IsTrue(File.Exists(source));
    }

    [TestMethod]
    public async Task Move_OnTheSameVolume_NeverChecksSpace()
    {
        var (source, target) = Files();
        var prompts = new ScriptedPrompts();
        var disk = new TinyDisk(0, _ => "C");

        await new ContentModelMoveFileTask(disk, disk, new Url(source), new Url(target), false, prompts).ExecuteAsync();

        Assert.AreEqual(0, prompts.Titles.Count);
        Assert.IsTrue(File.Exists(target));
        Assert.IsFalse(File.Exists(source));
    }

    [TestMethod]
    public async Task Move_ToAnotherVolume_WarnsWhenItDoesNotFit()
    {
        var (source, target) = Files();
        var prompts = new ScriptedPrompts(false);
        var disk = new TinyDisk(5, url => url.FullName.EndsWith("target.txt") ? "D" : "C");

        await new ContentModelMoveFileTask(disk, disk, new Url(source), new Url(target), false, prompts).ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "Not enough free space" }, prompts.Titles);
    }

    [TestMethod]
    public async Task CopyFolder_WithoutEnoughSpace_WarnsOnceForTheWholeTree()
    {
        var folder = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "0123456789");
        File.WriteAllText(Path.Combine(folder, "b.txt"), "0123456789");
        var prompts = new ScriptedPrompts(false);
        var disk = new TinyDisk(5, _ => "C");

        var task = new ContentModelCopyFolderTask(
            disk, disk, new Url(folder + Path.DirectorySeparatorChar), new Url(Path.Combine(_tempDir, "dst") + Path.DirectorySeparatorChar), true, null, prompts);
        await task.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "Not enough free space" }, prompts.Titles);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "dst", "b.txt")));
    }

    [TestMethod]
    public async Task MoveFolder_OnTheSameVolume_NeverChecksSpace()
    {
        var folder = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "0123456789");
        var prompts = new ScriptedPrompts();
        var disk = new TinyDisk(0, _ => "C");

        var task = new ContentModelMoveFolderTask(
            disk, disk, new Url(folder + Path.DirectorySeparatorChar), new Url(Path.Combine(_tempDir, "dst") + Path.DirectorySeparatorChar), true, null, prompts);
        await task.ExecuteAsync();

        Assert.AreEqual(0, prompts.Titles.Count);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "dst", "a.txt")));
    }
}
