using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Model;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Compare;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The folder synchroniser: the preview lists what would change, the options change it, and nothing runs without a plan.</summary>
[TestClass]
public class SynchronizeFoldersDialogTest
{
    private static readonly DateTime Noon = new(2024, 5, 6, 12, 0, 0);
    private string _root = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "sync-dialog-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "L"));
        Directory.CreateDirectory(Path.Combine(_root, "R"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private void Write(string side, string name, string content)
    {
        string path = Path.Combine(_root, side, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, Noon);
    }

    private static IContentModel Panel(string name, string? folder)
    {
        Mock<IContentModel> model = new();
        model.SetupGet(m => m.Name).Returns(name);
        if (folder is not null)
        {
            model.SetupGet(m => m.Url).Returns(new Url(folder + Path.DirectorySeparatorChar));
        }
        return model.Object;
    }

    private async Task<SynchronizeFoldersDialog> OpenAsync(bool bothPanels = true)
    {
        Mock<IContentModelService> panels = new();
        ConcurrentList<IContentModel> list = [];
        list.Add(Panel(ContentModelService.Left, bothPanels ? Path.Combine(_root, "L") : null));
        list.Add(Panel(ContentModelService.Right, bothPanels ? Path.Combine(_root, "R") : null));
        panels.SetupGet(p => p.ContentModels).Returns(list);
        FileSystemResolver resolver = new(new FileSystemService(), null!, null!, null!, panels.Object);
        var dialog = new SynchronizeFoldersDialog(panels.Object, resolver, TestTheme());
        await ShowAsync(dialog, new Size(100, 30));
        return dialog;
    }

    /// <summary>Makes a preview and lets its background work land, the way the idle pass would.</summary>
    private static async Task PreviewAsync(SynchronizeFoldersDialog dialog)
    {
        dialog.StartPreview();
        await dialog.WorkTask!;
        dialog.OnIdle();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task A_Preview_ListsWhatWouldBeCopied_BeforeAnythingIs()
    {
        Write("L", "only-left.txt", "x");
        var dialog = await OpenAsync();

        await PreviewAsync(dialog);

        Assert.IsNotNull(dialog.ShownPlan);
        Assert.AreEqual(1, dialog.ShownPlan.CopiesToRight);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "R", "only-left.txt")), "the preview copies nothing");
        Assert.IsTrue(dialog.Status.Contains("Enter runs it"), dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangingAnOption_ThrowsTheShownPreviewAway()
    {
        Write("L", "only-left.txt", "x");
        var dialog = await OpenAsync();
        await PreviewAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('b'));

        Assert.IsNull(dialog.ShownPlan, "a plan made with the old direction is not what would run");
        Assert.AreEqual(SyncDirection.LeftToRight, dialog.Direction);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheDirectionCyclesThroughBothWays_LeftToRight_RightToLeft()
    {
        var dialog = await OpenAsync();

        Assert.AreEqual(SyncDirection.Both, dialog.Direction);
        await dialog.HandleKeyEventAsync(Char('b'));
        Assert.AreEqual(SyncDirection.LeftToRight, dialog.Direction);
        await dialog.HandleKeyEventAsync(Char('b'));
        Assert.AreEqual(SyncDirection.RightToLeft, dialog.Direction);
        await dialog.HandleKeyEventAsync(Char('b'));
        Assert.AreEqual(SyncDirection.Both, dialog.Direction);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheOptionKeysSetSubfoldersAndDeleteExtras()
    {
        var dialog = await OpenAsync();
        bool subfolders = dialog.Subfolders;

        await dialog.HandleKeyEventAsync(Char('s'));
        await dialog.HandleKeyEventAsync(Char('x'));

        Assert.AreNotEqual(subfolders, dialog.Subfolders);
        Assert.IsTrue(dialog.DeleteExtras);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MatchingFolders_SayTheyMatch_AndNothingIsRun()
    {
        Write("L", "same.txt", "x");
        Write("R", "same.txt", "x");
        var dialog = await OpenAsync();

        await PreviewAsync(dialog);
        await dialog.RunAsync();

        Assert.AreEqual(0, dialog.ShownPlan!.Actions.Count);
        Assert.IsTrue(dialog.Status.Contains("nothing to synchronise", StringComparison.OrdinalIgnoreCase)
            || dialog.Status.Contains("match", StringComparison.OrdinalIgnoreCase), dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Deletions_NeedASecondRun_BeforeTheyStart()
    {
        Write("R", "extra.txt", "x");
        var dialog = await OpenAsync();
        // Deletions only happen in one-way synchronisation: left to right, the right's extra file goes.
        await dialog.HandleKeyEventAsync(Char('b'));
        await dialog.HandleKeyEventAsync(Char('x'));
        await PreviewAsync(dialog);
        Assert.AreEqual(1, dialog.ShownPlan!.Deletes);

        await dialog.RunAsync();

        Assert.IsTrue(dialog.Status.Contains("Enter again"), dialog.Status);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "R", "extra.txt")), "nothing is deleted on the first Enter");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutTheTaskServices_ARunSaysItIsNotAvailable()
    {
        Write("L", "only-left.txt", "x");
        var dialog = await OpenAsync();
        await PreviewAsync(dialog);

        await dialog.RunAsync();

        Assert.IsTrue(dialog.Status.Contains("not available"), dialog.Status);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "R", "only-left.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithAPanelThatShowsNoFolder_ThePreviewSaysSo()
    {
        var dialog = await OpenAsync(bothPanels: false);

        dialog.StartPreview();

        Assert.IsTrue(dialog.Status.Contains("Both panels must show a folder"), dialog.Status);
        Assert.IsNull(dialog.WorkTask);
    }
}
