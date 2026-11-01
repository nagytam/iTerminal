using iTerminal.Core.Services.Printing;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Printing;

[TestClass]
public class PrintServiceTest
{
    private string _root = null!;

    [TestInitialize]
    public void CreateSandbox() => _root = Directory.CreateTempSubdirectory("iTerminal.Printing.").FullName;

    [TestCleanup]
    public void DeleteSandbox()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "hello");
        return path;
    }

    private static PrintService Windows(IExternalProcessRunner runner, TimeSpan? timeout = null) => new(runner, timeout ?? PrintService.DefaultTimeout, windows: true);

    private static PrintService Cups(IExternalProcessRunner runner, TimeSpan? timeout = null) => new(runner, timeout ?? PrintService.DefaultTimeout, windows: false);

    /// <summary>A runner that never answers until it is cancelled — the real one then throws, as this does.</summary>
    private sealed class HangingRunner : IExternalProcessRunner
    {
        public async Task<ProcessOutput> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ProcessOutput(0, string.Empty, string.Empty);
        }

        public Task<ProcessOutput> RunToStreamAsync(string fileName, IReadOnlyList<string> arguments, Stream destination, string? workingDirectory = null, Func<Stream, Task>? writeStandardInput = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>The runner's own 30 s ceiling: it kills the tool and answers with exit code -1.</summary>
    private sealed class GivesUpRunner : IExternalProcessRunner
    {
        public Task<ProcessOutput> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessOutput(-1, string.Empty, $"{fileName} did not answer within 30 seconds."));

        public Task<ProcessOutput> RunToStreamAsync(string fileName, IReadOnlyList<string> arguments, Stream destination, string? workingDirectory = null, Func<Stream, Task>? writeStandardInput = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    #region CUPS
    [TestMethod]
    [Timeout(15_000)]
    public async Task Cups_ListsThePrinters_TheDefaultFirst()
    {
        var runner = new FakeProcessRunner().Answer("lpstat -p -d",
            "printer Office is idle.  enabled since Mon 01 Jan 2026 10:00:00\nprinter Lab disabled since Mon 01 Jan 2026 10:00:00 -\nsystem default destination: Lab\n");

        var listing = await Cups(runner).ListPrintersAsync();

        Assert.IsNull(listing.Failure);
        CollectionAssert.AreEqual(new[] { "Lab", "Office" }, listing.Printers.Select(p => p.Name).ToArray());
        Assert.IsTrue(listing.Printers[0].IsDefault);
        Assert.AreEqual("disabled", listing.Printers[0].Status);
        Assert.AreEqual("idle", listing.Printers[1].Status);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Cups_NoDestinations_IsAnEmptyListNotAFailure()
    {
        var runner = new FakeProcessRunner().Answer("lpstat -p -d", string.Empty, exitCode: 1, stderr: "lpstat: No destinations added.");

        var listing = await Cups(runner).ListPrintersAsync();

        Assert.IsNull(listing.Failure);
        Assert.AreEqual(0, listing.Printers.Count);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Cups_ToolMissing_IsASentence()
    {
        var runner = new FakeProcessRunner().Answer("lpstat", string.Empty, exitCode: -1, stderr: "lpstat is not installed or not on PATH.");

        var listing = await Cups(runner).ListPrintersAsync();

        Assert.AreEqual("lpstat is not installed or not on PATH.", listing.Failure);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Cups_Queue_ReadsJobsAndTellsHeldFromQueued()
    {
        var runner = new FakeProcessRunner().Answer("lpstat -l -o Office",
            "Office-12   ada   2048   Mon 01 Jan 2026 10:00:00\n\tqueued for Office\n" +
            "Office-13   bob   100   Mon 01 Jan 2026 10:01:00\n\tAlerts: job-hold-until-specified\n\tqueued for Office\n");

        var queue = await Cups(runner).GetQueueAsync("Office");

        Assert.IsNull(queue.Failure);
        Assert.AreEqual(2, queue.Jobs.Count);
        Assert.AreEqual(new PrintJob("Office", 12, "Job 12", "ada", 2048, PrintJobState.Queued), queue.Jobs[0]);
        Assert.AreEqual(PrintJobState.Held, queue.Jobs[1].State);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Cups_Print_PassesEverythingAsSeparateArguments()
    {
        var path = Write("a b.txt");
        var runner = new FakeProcessRunner().Answer("lp", "request id is Office-14 (1 file(s))\n");

        var result = await Cups(runner).PrintFileAsync("Office", path, 3);

        Assert.IsTrue(result.Success, result.Message);
        StringAssert.Contains(result.Message, "Office-14");
        var call = runner.Calls.Single();
        Assert.AreEqual("lp", call.FileName);
        CollectionAssert.AreEqual(new[] { "-d", "Office", "-n", "3", "--", path }, call.Arguments.ToArray());
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Cups_HoldResumeAndCancel_NameThePrinterAndTheId()
    {
        var runner = new FakeProcessRunner().Answer("lp", string.Empty).Answer("cancel", string.Empty);
        var service = Cups(runner);

        Assert.IsTrue((await service.HoldJobAsync("Office", 12)).Success);
        Assert.IsTrue((await service.ResumeJobAsync("Office", 12)).Success);
        Assert.IsTrue((await service.CancelJobAsync("Office", 12)).Success);

        CollectionAssert.AreEqual(new[] { "lp -i Office-12 -H hold", "lp -i Office-12 -H resume", "cancel Office-12" }, runner.Calls.Select(c => c.CommandLine).ToArray());
    }
    #endregion

    #region Windows
    [TestMethod]
    [Timeout(15_000)]
    public async Task Windows_ListsThePrinters_FromJson_AcceptingOneObjectOrAnArray()
    {
        var runner = new FakeProcessRunner().Answer("powershell", "[{\"Name\":\"Zed\",\"Default\":false,\"Status\":\"Ready\"},{\"Name\":\"Alpha\",\"Default\":true,\"Status\":\"Offline\"}]");

        var many = await Windows(runner).ListPrintersAsync();

        CollectionAssert.AreEqual(new[] { "Alpha", "Zed" }, many.Printers.Select(p => p.Name).ToArray());
        Assert.IsTrue(many.Printers[0].IsDefault);
        Assert.AreEqual("Offline", many.Printers[0].Status);

        var one = PrintService.ParseWindowsPrinters("{\"Name\":\"Solo\",\"Default\":true,\"Status\":\"Ready\"}");
        Assert.AreEqual(1, one.Count);
        Assert.AreEqual(0, PrintService.ParseWindowsPrinters("garbage").Count);
        Assert.AreEqual(0, PrintService.ParseWindowsPrinters("[]").Count);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Windows_Queue_NamesThePrinterInTheScript_AndMapsTheStates()
    {
        var runner = new FakeProcessRunner().Answer("powershell",
            "[{\"Id\":1,\"DocumentName\":\"a.txt\",\"UserName\":\"ada\",\"Size\":10,\"Status\":\"Normal\"}," +
            "{\"Id\":2,\"DocumentName\":\"b.txt\",\"UserName\":\"ada\",\"Size\":20,\"Status\":\"Paused\"}," +
            "{\"Id\":3,\"DocumentName\":\"c.txt\",\"UserName\":\"ada\",\"Size\":30,\"Status\":\"Printing\"}," +
            "{\"Id\":4,\"DocumentName\":\"d.txt\",\"UserName\":\"ada\",\"Size\":40,\"Status\":\"Error, PaperOut\"}]");

        var queue = await Windows(runner).GetQueueAsync("O'Brien's");

        CollectionAssert.AreEqual(
            new[] { PrintJobState.Queued, PrintJobState.Held, PrintJobState.Printing, PrintJobState.Error },
            queue.Jobs.Select(j => j.State).ToArray());
        Assert.IsTrue(queue.Jobs.All(j => j.Printer == "O'Brien's"));
        StringAssert.Contains(runner.Calls.Single().Arguments[^1], "Get-PrintJob -PrinterName 'O''Brien''s'");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Windows_JobActions_CarryThePrinterNameQuotedAndTheId()
    {
        var runner = new FakeProcessRunner().Answer("powershell", string.Empty);
        var service = Windows(runner);

        await service.HoldJobAsync("Front Desk", 7);
        await service.ResumeJobAsync("Front Desk", 7);
        await service.CancelJobAsync("Front Desk", 7);

        var scripts = runner.Calls.Select(c => c.Arguments[^1]).ToArray();
        StringAssert.EndsWith(scripts[0], "Suspend-PrintJob -PrinterName 'Front Desk' -ID 7");
        StringAssert.EndsWith(scripts[1], "Resume-PrintJob -PrinterName 'Front Desk' -ID 7");
        StringAssert.EndsWith(scripts[2], "Remove-PrintJob -PrinterName 'Front Desk' -ID 7");
        Assert.IsTrue(runner.Calls.All(c => c.FileName == "powershell"), "Windows PowerShell 5.1, not pwsh");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Windows_TextFile_GoesThroughOutPrinter_WithTheCopies()
    {
        var path = Write("notes.TXT");
        var runner = new FakeProcessRunner().Answer("powershell", string.Empty);

        var result = await Windows(runner).PrintFileAsync("Office", path, 2);

        Assert.IsTrue(result.Success, result.Message);
        var script = runner.Calls.Single().Arguments[^1];
        StringAssert.Contains(script, "1..2 | ForEach-Object");
        StringAssert.Contains(script, $"Get-Content -LiteralPath '{path}' -Raw | Out-Printer -Name 'Office'");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Windows_OtherFile_GoesThroughThePrintToVerb()
    {
        var path = Write("report.pdf");
        var runner = new FakeProcessRunner().Answer("powershell", string.Empty);

        await Windows(runner).PrintFileAsync("Office", path, 1);

        StringAssert.Contains(runner.Calls.Single().Arguments[^1], $"Start-Process -FilePath '{path}' -Verb PrintTo -ArgumentList '\"Office\"'");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Windows_PathWithQuotesDollarsAndSemicolons_StaysOneLiteral()
    {
        var path = Write("it's $(calc); done.txt");
        var runner = new FakeProcessRunner().Answer("powershell", string.Empty);

        var result = await Windows(runner).PrintFileAsync("Office", path, 1);

        Assert.IsTrue(result.Success, result.Message);
        var escaped = path.Replace("'", "''");
        StringAssert.Contains(runner.Calls.Single().Arguments[^1], $"-LiteralPath '{escaped}' -Raw");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Windows_PrinterWithADoubleQuote_PrintsTextOnly()
    {
        var runner = new FakeProcessRunner().Answer("powershell", string.Empty);
        var service = Windows(runner);

        var pdf = await service.PrintFileAsync("a\"b", Write("x.pdf"), 1);
        var text = await service.PrintFileAsync("a\"b", Write("x.txt"), 1);

        Assert.IsFalse(pdf.Success);
        Assert.IsTrue(text.Success, text.Message);
        Assert.AreEqual(1, runner.Calls.Count);
    }
    #endregion

    #region Refusals
    [TestMethod]
    [Timeout(15_000)]
    public async Task NothingTypedBecomesCode_OrAnOption()
    {
        var runner = new FakeProcessRunner().Answer("lp", string.Empty).Answer("cancel", string.Empty).Answer("lpstat", string.Empty).Answer("powershell", string.Empty);
        var path = Write("ok.txt");

        foreach (var service in new[] { Cups(runner), Windows(runner) })
        {
            Assert.IsFalse((await service.PrintFileAsync("-o", path, 1)).Success);
            Assert.IsFalse((await service.PrintFileAsync("", path, 1)).Success);
            Assert.IsFalse((await service.PrintFileAsync("Office\nx", path, 1)).Success);
            Assert.IsFalse((await service.PrintFileAsync("Office", "relative.txt", 1)).Success);
            Assert.IsFalse((await service.PrintFileAsync("Office", Path.Combine(_root, "missing.txt"), 1)).Success);
            Assert.IsFalse((await service.PrintFileAsync("Office", path + "\n", 1)).Success);
            Assert.IsFalse((await service.CancelJobAsync("-x", 1)).Success);
            Assert.IsFalse((await service.HoldJobAsync("Office", -1)).Success);
            Assert.IsNotNull((await service.GetQueueAsync("-o")).Failure);
        }

        Assert.AreEqual(0, runner.Calls.Count, "no tool was started for any refused request");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Copies_AreKeptBetweenOneAndNinetyNine()
    {
        var runner = new FakeProcessRunner().Answer("lp", string.Empty);
        var path = Write("ok.txt");

        await Cups(runner).PrintFileAsync("Office", path, 0);
        await Cups(runner).PrintFileAsync("Office", path, 5000);

        CollectionAssert.AreEqual(new[] { "1", "99" }, runner.Calls.Select(c => c.Arguments[3]).ToArray());
    }
    #endregion

    #region Timeouts
    [TestMethod]
    [Timeout(15_000)]
    public async Task ATool_ThatDoesNotAnswer_IsASentence_WhenOurBoundCancelsIt()
    {
        var service = Cups(new HangingRunner(), TimeSpan.FromMilliseconds(100));

        var listing = await service.ListPrintersAsync();
        var result = await service.CancelJobAsync("Office", 1);

        StringAssert.Contains(listing.Failure!, "did not answer");
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "did not answer");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ATool_TheRunnerGaveUpOn_IsASentence()
    {
        var listing = await Windows(new GivesUpRunner()).ListPrintersAsync();

        Assert.AreEqual("powershell did not answer within 30 seconds.", listing.Failure);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task TheCallersOwnCancellation_IsNotTurnedIntoAnAnswer()
    {
        using var cancellation = new CancellationTokenSource(50);

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => Cups(new HangingRunner()).ListPrintersAsync(cancellation.Token));
    }
    #endregion
}
