using System.Collections.Specialized;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.Test;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Rename;
using iTerminal.Core.Tasks;
using iTerminal.Core.Test.Helper;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands;

/// <summary>Multi-rename's batch: what Rename runs, what it leaves out, and what the read-only gate refuses.</summary>
[TestClass]
public class MultiRenameCommandTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "MultiRenameCommandTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    /// <summary>A read-only panel: an archive, a search or a preview. Entries are mocks, so a mark has a row to sit on.</summary>
    private sealed class ReadOnlyTestModel : ContentModelBase
    {
        public ReadOnlyTestModel(IFrequentExecutionGuardService guard, Url url)
            : base("Test", guard)
        {
            Url = url;
            ContentBrowser = Mock.Of<IContentBrowser>();
            ViewSize = new Size(40, 10);
        }

        public override bool IsReadOnly => true;
        public override string GetFooter() => string.Empty;
        public override string GetHeader(int width) => string.Empty;
        public override void ReadUrl() { }
        public override void Refresh(bool forceRefresh = false) { }
        public override void SetUrl(Url path) => Url = path;
    }

    private static Mock<IFrequentExecutionGuardService> Guard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        return guard;
    }

    private static Button RenameButton(MultiRenameDialog dialog) =>
        dialog.Controls.AsSnapshot().OfType<Button>().Single(button => button.Title == "Rename");

    /// <summary>
    /// Presses Rename and waits for the batch it queued. The task manager runs its own workers and
    /// removes a task once it has finished, so the batch is caught as it is added, not read back
    /// afterwards: a fast batch would already be gone. It is run once, by the workers, and not again here.
    /// </summary>
    private static async Task<IReadOnlyList<BatchOperationTask>> PressRenameAndWaitAsync(MultiRenameDialog dialog, TaskManager taskManager)
    {
        var added = new List<BatchOperationTask>();
        void OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is null)
            {
                return;
            }

            lock (added)
            {
                added.AddRange(e.NewItems.OfType<BatchOperationTask>());
            }
        }

        taskManager.Tasks.CollectionChanged += OnChanged;
        try
        {
            await RenameButton(dialog).TriggerHotKeyAsync();
            List<BatchOperationTask> batches;
            lock (added)
            {
                batches = [.. added];
            }

            var deadline = DateTime.UtcNow.AddSeconds(20);
            foreach (var batch in batches)
            {
                while (batch.Status == iConsole.Data.TaskStatus.Started && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(10);
                }
            }

            return batches;
        }
        finally
        {
            taskManager.Tasks.CollectionChanged -= OnChanged;
        }
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task RenameRunsTheMarkedFilesAsOneBatchAndLeavesTheRestAlone()
    {
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("a.txt"), true);
        model.SetMark(model.Row("b.txt"), true);
        var taskManager = new TaskManager();
        var dialog = MultiRenameDialogTest.NewDialog(taskManager, MultiRenameDialogTest.ServiceFor(model).Object);
        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));
        MultiRenameDialogTest.Type(dialog, "[C]_x");

        var batches = await PressRenameAndWaitAsync(dialog, taskManager);

        Assert.AreEqual(1, batches.Count, "one batch for the whole operation");
        Assert.AreEqual(2, batches[0].Steps.Count);
        Assert.AreEqual(2, batches[0].Succeeded, string.Join("; ", batches[0].Failures));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "a_x.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "b_x.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "a.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "c.jpg")), "an unmarked file is not touched");
        Assert.AreEqual(0, model.MarkedCount, "the marks are consumed");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task RenameMovesAMarkedFolderUnderItsNewName()
    {
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("photos"), true);
        var taskManager = new TaskManager();
        var dialog = MultiRenameDialogTest.NewDialog(taskManager, MultiRenameDialogTest.ServiceFor(model).Object);
        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));
        MultiRenameDialogTest.Type(dialog, "[C]_old");

        var batches = await PressRenameAndWaitAsync(dialog, taskManager);

        Assert.AreEqual(1, batches[0].Succeeded, string.Join("; ", batches[0].Failures));
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "photos_old")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "photos")));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ABlockedRowIsNotSubmittedSoRenameQueuesNothing()
    {
        // a -> b would take b's name, which b is giving up: blocked, and b stays unchanged. Nothing is Ok, so Rename queues no batch.
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("a.txt"), true);
        model.SetMark(model.Row("b.txt"), true);
        var taskManager = new TaskManager();
        var dialog = MultiRenameDialogTest.NewDialog(taskManager, MultiRenameDialogTest.ServiceFor(model).Object);
        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));
        MultiRenameDialogTest.Type(dialog, "[C]", find: "a", replace: "b");

        var batches = await PressRenameAndWaitAsync(dialog, taskManager);

        Assert.AreEqual(0, dialog.OkCount);
        Assert.AreEqual(0, batches.Count);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "a.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "b.txt")));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ATargetTakenAfterPlanningFailsItsStepAndIsNeverOverwritten()
    {
        // The plan saw no a_x.txt. Another program writes one before Rename runs: the step must fail, not replace it.
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("a.txt"), true);
        var taskManager = new TaskManager();
        var dialog = MultiRenameDialogTest.NewDialog(taskManager, MultiRenameDialogTest.ServiceFor(model).Object);
        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));
        MultiRenameDialogTest.Type(dialog, "[C]_x");
        Assert.AreEqual(1, dialog.OkCount, "planned as Ok before the other program writes the target");
        File.WriteAllText(Path.Combine(_root, "a_x.txt"), "keep");

        var batches = await PressRenameAndWaitAsync(dialog, taskManager);

        Assert.AreEqual(0, batches[0].Succeeded);
        Assert.AreEqual(1, batches[0].Failures.Count, "the step is listed as failed");
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(_root, "a_x.txt")));
        Assert.AreEqual("aaaa", File.ReadAllText(Path.Combine(_root, "a.txt")));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void TheMenuCommandIsRefusedOnAReadOnlyPanel()
    {
        var readOnly = new ReadOnlyTestModel(Guard().Object, new Url(_root + Path.DirectorySeparatorChar));
        var entry = MockEntry(Path.Combine(_root, "a.txt"), "a.txt");
        readOnly.Entries.Add(entry);
        readOnly.SetMark(entry, true);
        readOnly.Update();
        var service = MultiRenameDialogTest.ServiceFor(readOnly);
        var command = new MultiRenameDialogCommand(MultiRenameDialogTest.NewDialog(new TaskManager(), service.Object), service.Object);

        Assert.IsFalse(command.CanExecute(), "an archive, a search or a preview cannot take a move");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void TheMenuCommandIsOfferedOnAWritablePanelWithAMark()
    {
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("a.txt"), true);
        var service = MultiRenameDialogTest.ServiceFor(model);
        var command = new MultiRenameDialogCommand(MultiRenameDialogTest.NewDialog(new TaskManager(), service.Object), service.Object);

        Assert.IsTrue(command.CanExecute());
    }

    private static IEntry MockEntry(string path, string name)
    {
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(new Url(path));
        entry.Setup(e => e.DisplayName).Returns(name);
        entry.Setup(e => e.Name).Returns(name);
        return entry.Object;
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ACaseOnlyChangeIsNeverSubmitted()
    {
        // a.txt -> A.txt is refused on every platform: on a case-insensitive volume the engine would see the file as its own target.
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("a.txt"), true);
        var taskManager = new TaskManager();
        var dialog = MultiRenameDialogTest.NewDialog(taskManager, MultiRenameDialogTest.ServiceFor(model).Object);
        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));
        MultiRenameDialogTest.Type(dialog, "[C]", find: "a", replace: "A");
        Assert.AreEqual(RenameStatus.CaseOnly, dialog.Plan.Single().Status);

        var batches = await PressRenameAndWaitAsync(dialog, taskManager);

        Assert.AreEqual(0, batches.Count);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "a.txt")));
    }
}
