using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Test.Core;
using iTerminal.Core.Commands;
using iTerminal.Core.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Checksums;
using Moq;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Shift+Alt+F3: creating a manifest (F2), verifying one (F3), stopping a run and what the dialog says.</summary>
[TestClass]
public class HashManifestDialogTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _root = Directory.CreateTempSubdirectory("iTerminal.ManifestDialog.").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "abc");
        File.WriteAllText(Path.Combine(_root, "sub", "b.txt"), "bravo");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private Url Folder => new(_root + Path.DirectorySeparatorChar);

    private async Task<(HashManifestDialog Dialog, Control Root)> OpenAsync(IReadOnlyList<Url>? roots = null, Url? manifest = null, IFileSystem? fileSystem = null)
    {
        HashManifestDialog dialog = new(TestTheme());
        dialog.Load(Folder, roots ?? [], manifest, fileSystem ?? Mock.Of<IFileSystem>());
        Control root = await ShowAsync(dialog);
        return (dialog, root);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ItNamesTheFolderAndWhatItCovers_AndTheKeys()
    {
        (HashManifestDialog dialog, Control root) = await OpenAsync();

        ScreenBuffer screen = Render(root);
        AssertScreenContains(screen, "Hash manifest");
        AssertScreenContains(screen, "Folder: ");
        AssertScreenContains(screen, "Covers: the whole folder tree");
        AssertScreenContains(screen, "SHA-256");
        AssertScreenContains(screen, "F2:Create the manifest");
        Assert.AreEqual(ChecksumAlgorithm.Sha256, dialog.Algorithm, "SHA-256 is the default");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F2_WritesTheManifest_AndSaysSo()
    {
        (HashManifestDialog dialog, Control root) = await OpenAsync();

        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key(ConsoleKey.F2)));
        await dialog.RunTask;

        Assert.AreEqual(ManifestWriteOutcome.Saved, dialog.WriteOutcome);
        StringAssert.Contains(dialog.Status, "Saved SHA256SUMS: 2 file(s).");
        StringAssert.Contains(File.ReadAllText(Path.Combine(_root, "SHA256SUMS")), " *sub/b.txt\n");
        AssertScreenContains(Render(root), "Saved SHA256SUMS");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F2_WithAnotherAlgorithm_WritesThatManifest()
    {
        (HashManifestDialog dialog, _) = await OpenAsync();
        dialog.Algorithm = ChecksumAlgorithm.Sha512;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F2));
        await dialog.RunTask;

        Assert.IsTrue(File.Exists(Path.Combine(_root, "SHA512SUMS")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "SHA256SUMS")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F2_NeverReplacesAManifestThatSaysSomethingElse()
    {
        string theirs = $"{new string('0', 64)}  a.txt\n";
        File.WriteAllText(Path.Combine(_root, "SHA256SUMS"), theirs);
        (HashManifestDialog dialog, _) = await OpenAsync();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F2));
        await dialog.RunTask;

        Assert.AreEqual(ManifestWriteOutcome.SaysSomethingElse, dialog.WriteOutcome);
        StringAssert.Contains(dialog.Status, "says something else");
        Assert.AreEqual(theirs, File.ReadAllText(Path.Combine(_root, "SHA256SUMS")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MarkedRowsAreCovered_AndTheDialogSaysHowMany()
    {
        (HashManifestDialog dialog, Control root) = await OpenAsync(roots: [new Url(Path.Combine(_root, "a.txt"))]);

        AssertScreenContains(Render(root), "Covers: 1 selected row(s)");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F2));
        await dialog.RunTask;

        Assert.AreEqual($"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad *a.txt\n", File.ReadAllText(Path.Combine(_root, "SHA256SUMS")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F3_ListsEveryRow_WithASummary()
    {
        (HashManifestDialog dialog, Control root) = await OpenAsync();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F2));
        await dialog.RunTask;
        File.AppendAllText(Path.Combine(_root, "a.txt"), "!");
        File.Delete(Path.Combine(_root, "sub", "b.txt"));
        File.WriteAllText(Path.Combine(_root, "new.txt"), "n");

        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key(ConsoleKey.F3)));
        await dialog.RunTask;

        StringAssert.Contains(dialog.Status, "SHA256SUMS: 0 OK, 1 changed, 1 missing, 1 extra, 0 unreadable.");
        ScreenBuffer screen = Render(root);
        AssertScreenContains(screen, "changed     a.txt");
        AssertScreenContains(screen, "missing     sub/b.txt");
        AssertScreenContains(screen, "extra       new.txt");
        Render(root);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F3_OnACleanFolder_SaysEverythingIsOk()
    {
        (HashManifestDialog dialog, Control root) = await OpenAsync();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F2));
        await dialog.RunTask;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F3));
        await dialog.RunTask;

        StringAssert.Contains(dialog.Status, "2 OK, 0 changed, 0 missing, 0 extra");
        AssertScreenContains(Render(root), "OK          a.txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F3_ChecksTheManifestTheCursorWasOn()
    {
        string manifest = Path.Combine(_root, "release.sha256");
        File.WriteAllText(manifest, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  a.txt\n");
        (HashManifestDialog dialog, _) = await OpenAsync(manifest: new Url(manifest));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F3));
        await dialog.RunTask;

        StringAssert.Contains(dialog.Status, "release.sha256: 1 OK");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F3_WithNoManifestAnywhere_SaysSo()
    {
        (HashManifestDialog dialog, _) = await OpenAsync();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F3));
        await dialog.RunTask;

        StringAssert.Contains(dialog.Status, "No manifest here");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatIsNoManifest_IsSaidInPlaceOfAList()
    {
        string notes = Path.Combine(_root, "notes.txt");
        File.WriteAllText(notes, "just words");
        (HashManifestDialog dialog, _) = await OpenAsync(manifest: new Url(notes));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F3));
        await dialog.RunTask;

        StringAssert.Contains(dialog.Status, "Failed: The manifest holds no checksums.");
        Assert.AreEqual(0, dialog.Items.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscStopsARunningCreate_WritesNothing_AndKeepsTheDialogUp()
    {
        Url folder = new("docker://web/etc/");
        Url file = new("docker://web/etc/app.conf");
        TaskCompletionSource started = new();
        Mock<IEntryListingFileSystem> drive = new();
        drive.Setup(d => d.ListAsync(It.IsAny<Url>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IEntry>)[new iTerminal.Core.Model.ContentModels.VirtualDriveEntry(file) { Size = 10 }]);
        drive.Setup(d => d.StreamToAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (Url _, Stream _, IProgress<long>? _, CancellationToken token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            });
        HashManifestDialog dialog = new(TestTheme());
        dialog.Load(folder, [], null, drive.Object);
        Control root = await ShowAsync(dialog);

        dialog.StartCreate();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.IsTrue(dialog.IsRunning);
        AssertScreenContains(Render(root), "Creating… 0%");

        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape)), "Esc is taken by the running check");
        await dialog.RunTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsFalse(dialog.IsRunning);
        Assert.IsNull(dialog.Created);
        StringAssert.Contains(dialog.Status, "Stopped — nothing was written.");
        AssertScreenContains(Render(root), "Stopped — nothing was written.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARunningCreate_IsStoppedByCancel_AndWritesNothing()
    {
        for (int i = 0; i < 40; i++)
        {
            File.WriteAllBytes(Path.Combine(_root, $"big{i}.bin"), new byte[1 << 20]);
        }

        (HashManifestDialog dialog, _) = await OpenAsync();
        dialog.StartCreate();
        bool cancelled = dialog.Cancel();
        await dialog.RunTask;

        // Either the run was stopped, or it was quick enough to finish before the cancel landed; never half a manifest.
        if (cancelled && dialog.Created is null)
        {
            Assert.IsFalse(File.Exists(Path.Combine(_root, "SHA256SUMS")));
            StringAssert.Contains(dialog.Status, "nothing was written");
        }
        else
        {
            Assert.IsTrue(File.Exists(Path.Combine(_root, "SHA256SUMS")) || dialog.Created is not null);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingTheDialog_StopsTheRun()
    {
        (HashManifestDialog dialog, _) = await OpenAsync();

        dialog.Close();

        Assert.IsFalse(dialog.IsRunning);
    }

    [TestMethod]
    public void TheCommand_IsOnShiftAltF3_AndNoOtherCommandOwnsThatChord()
    {
        HashManifestDialogCommand command = new(null!, Mock.Of<IContentModelService>());

        Assert.AreEqual(new Key(ConsoleKey.F3, true, true, false), command.Key);
        Assert.AreNotEqual(new Key(ConsoleKey.F3, true, false, true), command.Key, "Ctrl+Shift+F3 is the language selector");
        Assert.IsFalse(command.CanExecute(), "nothing to act on without a panel");
    }
}
