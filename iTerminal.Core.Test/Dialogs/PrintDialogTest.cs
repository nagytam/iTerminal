using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Printing;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class PrintDialogTest
{
    private static readonly Printer Office = new("Office", true, "idle");
    private static readonly Printer Lab = new("Lab", false, "idle");
    private static readonly PrintJob Queued = new("Office", 12, "budget.xlsx", "ada", 2048, PrintJobState.Queued);
    private static readonly PrintJob Held = new("Office", 13, "letter.docx", "bob", 100, PrintJobState.Held);

    private Mock<IPrintService> _service = null!;
    private Mock<ISettingsService> _settings = null!;
    private PrintDialog _dialog = null!;
    private int _queueReads;

    [TestInitialize]
    public void Setup()
    {
        _service = new Mock<IPrintService>();
        _service.Setup(s => s.ListPrintersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new PrinterListing([Office, Lab]));
        _service.Setup(s => s.GetQueueAsync("Office", It.IsAny<CancellationToken>()))
            .Returns(() => { Interlocked.Increment(ref _queueReads); return Task.FromResult(new QueueListing([Queued, Held])); });
        _service.Setup(s => s.GetQueueAsync("Lab", It.IsAny<CancellationToken>())).ReturnsAsync(new QueueListing([]));
        _service.Setup(s => s.PrintFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintResult(true, "Sent."));
        _service.Setup(s => s.HoldJobAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PrintResult(true, "Job held."));
        _service.Setup(s => s.ResumeJobAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PrintResult(true, "Job resumed."));
        _service.Setup(s => s.CancelJobAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PrintResult(true, "Job cancelled."));
        _settings = new Mock<ISettingsService>();
        _dialog = new PrintDialog(_service.Object, _settings.Object, UiTestHelper.TestTheme()) { PollInterval = TimeSpan.FromHours(1) };
    }

    private async Task OpenAsync(string? file = @"C:\data\budget.pdf")
    {
        _dialog.Load(file);
        await UiTestHelper.ShowAsync(_dialog);
        await UiTestHelper.WaitUntilAsync(() => _dialog.JobCountForTest == 2, "the queue to be read");
    }

    private iConsole.Test.Core.ScreenBuffer Screen()
    {
        _dialog.OnIdle();
        return UiTestHelper.Render(_dialog);
    }

    private Task PressAsync(ConsoleKey key, char keyChar = '\0', bool shift = false) =>
        _dialog.HandleKeyEventAsync(UiTestHelper.Key(key, keyChar, shift));

    [TestMethod]
    [Timeout(15_000)]
    public async Task Opening_ShowsTheFile_ThePrinterAndItsQueue()
    {
        await OpenAsync();

        var screen = Screen();
        UiTestHelper.AssertScreenContains(screen, "budget.pdf");
        UiTestHelper.AssertScreenContains(screen, "Office (default)");
        UiTestHelper.AssertScreenContains(screen, "budget.xlsx");
        UiTestHelper.AssertScreenContains(screen, "Held");
        UiTestHelper.AssertScreenContains(screen, "Queued");
        Screen();
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Enter_PrintsTheFile_WithTheChosenCopies_AndReadsTheQueueAgain()
    {
        await OpenAsync();
        await PressAsync(ConsoleKey.OemPlus, '+', shift: true);
        await PressAsync(ConsoleKey.OemPlus, '+', shift: true);
        var reads = _queueReads;

        await PressAsync(ConsoleKey.Enter);
        await UiTestHelper.WaitUntilAsync(() => _dialog.StatusForTest == "Sent.", "the print result");

        _service.Verify(s => s.PrintFileAsync("Office", @"C:\data\budget.pdf", 3, It.IsAny<CancellationToken>()), Times.Once);
        await UiTestHelper.WaitUntilAsync(() => _queueReads > reads, "the queue to be read after the print");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WithoutAFile_EnterPrintsNothing_AndSaysSo()
    {
        await OpenAsync(file: null);

        await PressAsync(ConsoleKey.Enter);

        _service.Verify(s => s.PrintFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        StringAssert.Contains(_dialog.StatusForTest, "no file");
        UiTestHelper.AssertScreenContains(Screen(), "the queue only");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task P_HoldsAQueuedJob_AndResumesAHeldOne()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.P, 'p');
        await UiTestHelper.WaitUntilAsync(() => _dialog.StatusForTest == "Job held.", "the hold result");
        await PressAsync(ConsoleKey.DownArrow);
        await PressAsync(ConsoleKey.P, 'p');
        await UiTestHelper.WaitUntilAsync(() => _dialog.StatusForTest == "Job resumed.", "the resume result");

        _service.Verify(s => s.HoldJobAsync("Office", 12, It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.ResumeJobAsync("Office", 13, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Delete_Asks_AndOnlyYCancels()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.Delete);
        StringAssert.Contains(_dialog.StatusForTest, "Cancel job 12");
        await PressAsync(ConsoleKey.N, 'n');
        _service.Verify(s => s.CancelJobAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        await PressAsync(ConsoleKey.Delete);
        await PressAsync(ConsoleKey.Y, 'y');
        await UiTestHelper.WaitUntilAsync(() => _dialog.StatusForTest == "Job cancelled.", "the cancel result");

        _service.Verify(s => s.CancelJobAsync("Office", 12, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ChoosingAnotherPrinter_ReadsItsQueue_AndRemembersIt()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.RightArrow);
        await UiTestHelper.WaitUntilAsync(() => _dialog.PrinterForTest == "Lab" && _dialog.JobCountForTest == 0, "the other printer's queue");

        _settings.Verify(s => s.Set(PrintDialog.PrinterSetting, "Lab"), Times.Once);
        UiTestHelper.AssertScreenDoesNotContain(Screen(), "budget.xlsx");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task TheRememberedPrinter_IsPreferredToTheDefault()
    {
        _settings.Setup(s => s.Get(PrintDialog.PrinterSetting)).Returns("lab");

        _dialog.Load(null);
        await UiTestHelper.ShowAsync(_dialog);
        await UiTestHelper.WaitUntilAsync(() => _dialog.PrinterForTest == "Lab", "the remembered printer");
    }

    [TestMethod]
    public void TheRememberedPrinterSetting_IsNotAReservedKey()
    {
        Assert.IsFalse(PrintDialog.PrinterSetting.StartsWith("__", StringComparison.Ordinal));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task AnAnswerForAPrinterNoLongerChosen_IsDropped()
    {
        var slow = new TaskCompletionSource<QueueListing>();
        _service.Setup(s => s.GetQueueAsync("Office", It.IsAny<CancellationToken>())).Returns(slow.Task);
        _dialog.Load(null);
        await UiTestHelper.ShowAsync(_dialog);
        await UiTestHelper.WaitUntilAsync(() => _dialog.PrinterForTest == "Office", "the printers");

        await PressAsync(ConsoleKey.RightArrow);
        await UiTestHelper.WaitUntilAsync(() => _dialog.PrinterForTest == "Lab", "the other printer");
        slow.SetResult(new QueueListing([Queued, Held]));
        await _dialog.PendingOperation;
        await Task.Delay(100);

        Assert.AreEqual(0, _dialog.JobCountForTest, "the old printer's queue must not land on the new printer");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task APollRound_IsSkipped_WhileACallIsStillOut()
    {
        var slow = new TaskCompletionSource<QueueListing>();
        var calls = 0;
        _service.Setup(s => s.GetQueueAsync("Office", It.IsAny<CancellationToken>())).Returns(() => { Interlocked.Increment(ref calls); return slow.Task; });
        _dialog.PollInterval = TimeSpan.Zero;
        _dialog.Load(null);
        await UiTestHelper.ShowAsync(_dialog);
        await UiTestHelper.WaitUntilAsync(() => Volatile.Read(ref calls) == 1, "the first read");

        for (var i = 0; i < 5; i++)
        {
            _dialog.OnIdle();
        }

        await Task.Delay(100);
        Assert.AreEqual(1, Volatile.Read(ref calls));
        slow.SetResult(new QueueListing([Queued]));
        await UiTestHelper.WaitUntilAsync(() => _dialog.JobCountForTest == 1, "the queue");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task AFailingTool_ShowsItsSentence()
    {
        _service.Setup(s => s.ListPrintersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new PrinterListing([], "lpstat is not installed or not on PATH."));

        _dialog.Load(null);
        await UiTestHelper.ShowAsync(_dialog);
        await UiTestHelper.WaitUntilAsync(() => _dialog.PendingOperation.IsCompleted, "the printers");
        await Task.Delay(100);

        UiTestHelper.AssertScreenContains(Screen(), "lpstat is not installed");
    }

    [TestMethod]
    public void KeyHints_AreNamed_WithoutDuplicates()
    {
        var keys = _dialog.GetKeyHints().Select(h => h.Key).ToList();

        Assert.IsTrue(keys.Count > 0);
        CollectionAssert.AreEquivalent(keys.Distinct().ToList(), keys);
        CollectionAssert.IsSubsetOf(new[] { "Enter", "P", "Delete", "F5" }, keys);
    }
}
