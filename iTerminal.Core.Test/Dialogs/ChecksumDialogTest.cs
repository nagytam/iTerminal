using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Checksums;
using Moq;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Shift+F12: reading a file, comparing a typed checksum, saving name.sha256 and fitting a narrow terminal.</summary>
// Some tests put text on the ambient clipboard and read it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class ChecksumDialogTest
{
    private static readonly ChecksumResult Hashes = new(
        new Dictionary<ChecksumAlgorithm, string>
        {
            [ChecksumAlgorithm.Md5] = new('a', 32),
            [ChecksumAlgorithm.Sha1] = new('b', 40),
            [ChecksumAlgorithm.Sha256] = new('c', 64),
            [ChecksumAlgorithm.Sha512] = new('d', 128),
        },
        2048);

    private string _folder = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _folder = Path.Combine(Path.GetTempPath(), "checksum-dialog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, true);

    private async Task<(ChecksumDialog Dialog, Control Root)> OpenAsync(ChecksumResult? result, Exception? failure = null, string fileName = "data.bin", Size? screen = null)
    {
        string path = Path.Combine(_folder, fileName);
        File.WriteAllText(path, "x");
        Mock<IChecksumService> service = new();
        if (failure is not null)
        {
            service.Setup(s => s.ComputeAsync(It.IsAny<Url>(), It.IsAny<IFileSystem>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        }
        else
        {
            service.Setup(s => s.ComputeAsync(It.IsAny<Url>(), It.IsAny<IFileSystem>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>())).ReturnsAsync(result!);
        }

        ChecksumDialog dialog = new(service.Object, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load(new Url(path), Mock.Of<IFileSystem>());
        Control root = await ShowAsync(dialog, screen);
        await dialog.ComputeTask;
        dialog.OnIdle();
        return (dialog, root);
    }

    [TestMethod]
    public async Task ItShowsTheSizeAndEveryChecksum()
    {
        (ChecksumDialog dialog, Control root) = await OpenAsync(Hashes);

        ScreenBuffer screen = Render(root);
        AssertScreenContains(screen, "Checksums");
        AssertScreenContains(screen, "data.bin");
        AssertScreenContains(screen, new string('a', 32));
        AssertScreenContains(screen, new string('c', 64));
        AssertScreenContains(screen, "SHA-512");
        StringAssert.Contains(dialog.Details, new string('d', 66));
        AssertScreenContains(screen, "F2:save .sha256");
    }

    [TestMethod]
    public async Task AFailedRead_IsSaidInPlaceOfTheChecksums()
    {
        (ChecksumDialog dialog, _) = await OpenAsync(null, new IOException("disk gone"));

        Assert.AreEqual("disk gone", dialog.Error);
        StringAssert.Contains(dialog.Details, "Failed");
        StringAssert.Contains(dialog.Details, "disk gone");
    }

    [TestMethod]
    public async Task ATypedChecksum_IsComparedByItsLength()
    {
        (ChecksumDialog dialog, _) = await OpenAsync(Hashes);

        dialog.Compare = new string('c', 64);
        dialog.OnIdle();
        StringAssert.Contains(dialog.Verdict, "Matches the SHA-256");

        dialog.Compare = new string('C', 63) + "e";
        dialog.OnIdle();
        StringAssert.Contains(dialog.Verdict, "Does NOT match the SHA-256");

        dialog.Compare = "abc";
        dialog.OnIdle();
        StringAssert.Contains(dialog.Verdict, "Not a checksum yet: 3 characters");

        dialog.Compare = string.Empty;
        dialog.OnIdle();
        Assert.AreEqual(string.Empty, dialog.Verdict);
    }

    [TestMethod]
    public void CompareVerdict_WaitsForTheFile_AndIgnoresSpacesAndCase()
    {
        (string Text, bool? Matches)? waiting = ChecksumDialog.CompareVerdict(new string('a', 32), null);
        StringAssert.Contains(waiting!.Value.Text, "compared as soon as the file is read");
        Assert.IsNull(waiting.Value.Matches);

        (string Text, bool? Matches)? spaced = ChecksumDialog.CompareVerdict(" " + new string('A', 32) + "\n", Hashes);
        Assert.IsTrue(spaced!.Value.Matches);
        Assert.IsNull(ChecksumDialog.CompareVerdict("  ", Hashes));
    }

    [TestMethod]
    public async Task F2_WritesTheSha256File_ThenSaysItAlreadyDoes_AndNeverOverwritesADifferentOne()
    {
        (ChecksumDialog dialog, _) = await OpenAsync(Hashes);
        string target = Path.Combine(_folder, "data.bin.sha256");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F2));
        StringAssert.Contains(dialog.Verdict, "Saved data.bin.sha256");
        Assert.AreEqual(new string('c', 64) + " *data.bin\n", File.ReadAllText(target));

        Assert.IsTrue(dialog.SaveChecksumFile());
        StringAssert.Contains(dialog.Verdict, "already says so");

        File.WriteAllText(target, new string('f', 64) + " *data.bin\n");
        Assert.IsFalse(dialog.SaveChecksumFile());
        StringAssert.Contains(dialog.Verdict, "not overwritten");
        Assert.AreEqual(new string('f', 64) + " *data.bin\n", File.ReadAllText(target));
    }

    [TestMethod]
    public async Task TypingAfterASave_ClearsWhatSaveSaid()
    {
        (ChecksumDialog dialog, _) = await OpenAsync(Hashes);
        dialog.SaveChecksumFile();

        await dialog.HandleKeyEventAsync(Char('a'));

        Assert.IsFalse(dialog.Verdict.Contains("Saved"));
    }

    [TestMethod]
    public async Task SaveBeforeTheFileIsRead_SaysSo()
    {
        Mock<IChecksumService> service = new();
        TaskCompletionSource<ChecksumResult> never = new();
        service.Setup(s => s.ComputeAsync(It.IsAny<Url>(), It.IsAny<IFileSystem>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>())).Returns(never.Task);
        string path = Path.Combine(_folder, "slow.bin");
        File.WriteAllText(path, "x");
        ChecksumDialog dialog = new(service.Object, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load(new Url(path), Mock.Of<IFileSystem>());
        Control root = await ShowAsync(dialog);

        Assert.IsFalse(dialog.SaveChecksumFile());
        StringAssert.Contains(dialog.Verdict, "still being read");
        AssertScreenContains(Render(root), "Reading");

        dialog.Close();
        never.TrySetCanceled();
    }

    [TestMethod]
    public async Task AChecksumWrittenBesideTheFile_IsCheckedWithoutAsking()
    {
        File.WriteAllText(Path.Combine(_folder, "data.bin.sha256"), new string('c', 64) + " *data.bin\n");
        (ChecksumDialog dialog, Control root) = await OpenAsync(Hashes);

        Assert.IsNotNull(dialog.Written);
        AssertScreenContains(Render(root), "✓ matches");
    }

    [TestMethod]
    public async Task AWrittenChecksumThatDiffers_IsFlagged()
    {
        File.WriteAllText(Path.Combine(_folder, "data.bin.sha256"), new string('9', 64) + " *data.bin\n");
        (_, Control root) = await OpenAsync(Hashes);

        AssertScreenContains(Render(root), "does NOT match");
    }

    [TestMethod]
    public void InfoHeight_GrowsAsTheDialogNarrows()
    {
        Assert.AreEqual(10, ChecksumDialog.InfoHeight(66));
        Assert.IsTrue(ChecksumDialog.InfoHeight(36) > ChecksumDialog.InfoHeight(66));
    }

    [TestMethod]
    public async Task OnANarrowTerminal_TheWrittenVerdictIsStillOnScreen()
    {
        File.WriteAllText(Path.Combine(_folder, "data.bin.sha256"), new string('c', 64) + " *data.bin\n");
        Size small = new(54, 30);
        (ChecksumDialog dialog, Control root) = await OpenAsync(Hashes, screen: small);

        ScreenBuffer screen = Render(root, small);
        AssertScreenContains(screen, "✓ matches");
        AssertScreenContains(screen, "F2:save");
        Assert.IsTrue(dialog.ViewSize.Height > 17);
    }

    [TestMethod]
    public async Task EscapeCloses()
    {
        (ChecksumDialog dialog, _) = await OpenAsync(Hashes);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(dialog.IsVisible);
    }

    [TestMethod]
    public async Task Enter_DismissesTheReport_WhenNothingIsTyped_AndLeavesTheBoxAloneWhenSomethingIs()
    {
        (ChecksumDialog dialog, _) = await OpenAsync(Hashes);
        dialog.Compare = new string('c', 64);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.IsTrue(dialog.IsVisible);

        dialog.Compare = string.Empty;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsFalse(dialog.IsVisible);
    }

    private static MouseEvent ClickOnRow(ChecksumDialog dialog, int row) => new()
    {
        Position = new iConsole.Data.Position(dialog.AbsolutePosition.X + 2 + 14, dialog.AbsolutePosition.Y + 1 + row),
        LeftClicked = true,
    };

    private static int RowOf(ChecksumDialog dialog, string startsWith) =>
        dialog.Details.Split(Environment.NewLine).ToList().FindIndex(line => line.StartsWith(startsWith, StringComparison.Ordinal));

    [TestMethod]
    public async Task AClickOnAChecksum_CopiesItToTheClipboard_AndSaysSo()
    {
        (ChecksumDialog dialog, Control root) = await OpenAsync(Hashes);

        Assert.IsTrue(await dialog.HandleMouseEventAsync(ClickOnRow(dialog, RowOf(dialog, "MD5"))));
        Assert.AreEqual(new string('a', 32), await iConsole.Services.TextClipboard.Current.GetTextAsync());
        StringAssert.Contains(dialog.Verdict, "Copied the MD5 to the clipboard.");
        AssertScreenContains(Render(root), "Copied the MD5");

        // A SHA-512 is drawn over two rows; either one copies the whole checksum.
        int sha512 = RowOf(dialog, "SHA-512");
        await dialog.HandleMouseEventAsync(ClickOnRow(dialog, sha512 + 1));
        Assert.AreEqual(new string('d', 128), await iConsole.Services.TextClipboard.Current.GetTextAsync());
    }

    [TestMethod]
    public async Task AClickOnTheFileLineOrAGapCopiesNothing()
    {
        (ChecksumDialog dialog, _) = await OpenAsync(Hashes);
        await iConsole.Services.TextClipboard.Current.SetTextAsync("before");

        await dialog.HandleMouseEventAsync(ClickOnRow(dialog, 0));
        await dialog.HandleMouseEventAsync(ClickOnRow(dialog, 2));

        Assert.AreEqual("before", await iConsole.Services.TextClipboard.Current.GetTextAsync());
    }

    [TestMethod]
    public async Task F3_CopiesTheSha256()
    {
        (ChecksumDialog dialog, _) = await OpenAsync(Hashes);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F3));

        Assert.AreEqual(new string('c', 64), await iConsole.Services.TextClipboard.Current.GetTextAsync());
        StringAssert.Contains(dialog.Verdict, "Copied the SHA-256");
    }

    [TestMethod]
    public async Task F3_BeforeTheFileIsRead_SaysThereIsNothingToCopy()
    {
        (ChecksumDialog dialog, _) = await OpenAsync(null, new IOException("disk gone"));
        await iConsole.Services.TextClipboard.Current.SetTextAsync("before");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F3));

        StringAssert.Contains(dialog.Verdict, "Nothing to copy yet");
        Assert.AreEqual("before", await iConsole.Services.TextClipboard.Current.GetTextAsync());
    }

    [TestMethod]
    public async Task TheFooterNamesTheCopy_AndTypingInTheCompareBoxClearsTheCopiedNotice()
    {
        (ChecksumDialog dialog, Control root) = await OpenAsync(Hashes);
        AssertScreenContains(Render(root), "F3 or click:copy");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F3));
        StringAssert.Contains(dialog.Verdict, "Copied");

        await dialog.HandleKeyEventAsync(Char('a'));

        Assert.IsFalse(dialog.Verdict.Contains("Copied"));
    }
}
