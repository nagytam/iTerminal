using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Executables;
using iConsole.Services;
using iConsole.Services.Disassembly;

namespace iConsole.Test.Dialogs;

[TestClass]
public class ExecutableAnalysisDialogTest
{
    private sealed class FakeAnalyzer : IExecutableAnalyzer
    {
        public Func<string, CancellationToken, ExecutableReport> Handler { get; set; } = (_, _) => Report();

        public List<string> Paths { get; } = [];

        public ExecutableReport Analyze(string path, CancellationToken cancellationToken)
        {
            Paths.Add(path);
            return Handler(path, cancellationToken);
        }
    }

    private static ExecutableReport Report() => new()
    {
        Format = ExecutableFormat.Elf,
        FormatName = "ELF64 little-endian",
        Architecture = "x86-64",
        Kind = ExecutableKind.PieExecutable,
        Subsystem = "Linux",
        Hardening = [new ExecutableFlag("PIE", true), new ExecutableFlag("RELRO", false), new ExecutableFlag("Stack canary", null)],
        Imports = [new ExecutableImport("libc.so.6", -1)],
        ExportedSymbols = 2,
        Exports = ["main", "helper"],
        Sections = [new ExecutableSection(".text", 4096, 4096, 5.5, true, false, false)],
        Hints = [new ExecutableHint(ExecutableHintKind.HighEntropySection, ".data")],
        FileSize = 12_345,
    };

    private static ExecutableAnalysisDialog Create(FakeAnalyzer analyzer) =>
        new(analyzer, new ExecutableDisassembler(), new ThemeService()) { Url = new Url("file:///usr/bin/sample") };

    private static async Task LandAsync(ExecutableAnalysisDialog dialog)
    {
        try
        {
            await dialog.AnalysisTask!;
        }
        catch
        {
            // A failed read is handled by OnIdle, which is what is under test.
        }

        dialog.OnIdle();
    }

    private static string[] Row(ExecutableAnalysisDialog dialog, int row) => [dialog.Grid.CellAt(row, 0), dialog.Grid.CellAt(row, 1)];

    private static IEnumerable<string[]> Rows(ExecutableAnalysisDialog dialog) =>
        Enumerable.Range(0, dialog.Grid.RowCount).Select(r => Row(dialog, r));

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowsTheReportAsRowsAndSummarisesItOnTheStatusLine()
    {
        var dialog = Create(new FakeAnalyzer());
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        Assert.AreEqual(string.Format("ELF64 little-endian, x86-64, {0:N0} bytes", 12_345), dialog.Status);
        string[][] rows = [.. Rows(dialog)];
        CollectionAssert.Contains(rows.Select(r => r[0]).ToArray(), "PIE");
        Assert.AreEqual("yes", rows.Single(r => r[0] == "PIE")[1]);
        Assert.AreEqual("no", rows.Single(r => r[0] == "RELRO")[1]);
        Assert.AreEqual("n/a or unknown", rows.Single(r => r[0] == "Stack canary")[1]);
        Assert.IsTrue(rows.Any(r => r[0] == "  libc.so.6"));
        Assert.IsTrue(rows.Any(r => r[0] == "  main"));
        Assert.IsTrue(rows.Any(r => r[0] == ".text" && r[1].Contains("5.50")));
        Assert.IsTrue(rows.Any(r => r[1].StartsWith("Section .data is close to random")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheAnalyzerRunsOnAWorkerThreadAndNothingIsShownUntilAnIdlePass()
    {
        int dialogThread = Environment.CurrentManagedThreadId;
        int analyzerThread = -1;
        var analyzer = new FakeAnalyzer
        {
            Handler = (_, _) =>
            {
                analyzerThread = Environment.CurrentManagedThreadId;
                return Report();
            },
        };
        var dialog = Create(analyzer);

        await dialog.OnInitializeAsync();
        await dialog.AnalysisTask!;

        Assert.AreNotEqual(dialogThread, analyzerThread);
        Assert.AreEqual(0, dialog.Grid.RowCount, "the result lands on an idle pass, not on the worker");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatCannotBeRead_SaysWhyAndShowsNoRows()
    {
        var dialog = Create(new FakeAnalyzer { Handler = (_, _) => throw new ExecutableAnalysisException("The file is cut short.") });
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        Assert.AreEqual("The file is cut short.", dialog.Status);
        Assert.AreEqual(0, dialog.Grid.RowCount);
        Assert.IsNull(dialog.Report);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnexpectedFailure_IsReportedWithoutThrowing()
    {
        var dialog = Create(new FakeAnalyzer { Handler = (_, _) => throw new InvalidOperationException("boom") });
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        Assert.AreEqual("The file could not be analysed: boom", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingTheDialogCancelsTheReadAndDropsItsResult()
    {
        CancellationToken seen = default;
        using var started = new ManualResetEventSlim();
        var analyzer = new FakeAnalyzer
        {
            Handler = (_, token) =>
            {
                seen = token;
                started.Set();
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                token.ThrowIfCancellationRequested();
                return Report();
            },
        };
        var dialog = Create(analyzer);
        await dialog.OnInitializeAsync();
        started.Wait(TimeSpan.FromSeconds(30));
        Task<ExecutableReport> task = dialog.AnalysisTask!;
        string statusBeforeClose = dialog.Status;

        dialog.Close();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }

        dialog.OnIdle();

        Assert.IsTrue(seen.IsCancellationRequested);
        Assert.AreEqual(0, dialog.Grid.RowCount);
        Assert.AreEqual(statusBeforeClose, dialog.Status, "a cancelled read writes nothing to the status line");
        Assert.IsNull(dialog.Report);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningAnotherFile_ReplacesTheRowsOfTheFirst()
    {
        var analyzer = new FakeAnalyzer();
        var dialog = Create(analyzer);
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);
        Assert.IsTrue(dialog.Grid.RowCount > 0);

        analyzer.Handler = (_, _) => throw new ExecutableAnalysisException("not an executable");
        dialog.Url = new Url("file:///usr/bin/other");
        await dialog.OnInitializeAsync();
        Assert.AreEqual(0, dialog.Grid.RowCount, "nothing of the last file stays while the next is read");
        await LandAsync(dialog);

        CollectionAssert.AreEqual(new[] { "/usr/bin/sample", "/usr/bin/other" }, analyzer.Paths);
        Assert.AreEqual("not an executable", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutAUrl_SaysThereIsNothingToShow()
    {
        var analyzer = new FakeAnalyzer();
        var dialog = new ExecutableAnalysisDialog(analyzer, new ExecutableDisassembler(), new ThemeService());

        await dialog.OnInitializeAsync();

        Assert.AreEqual("Nothing to show", dialog.Status);
        Assert.AreEqual(0, analyzer.Paths.Count);
    }

    [TestMethod]
    public void ALongValue_ContinuesOnTheRowsBelowInsteadOfBeingCut()
    {
        string subject = "CN=" + new string('x', 100);
        ExecutableReport report = new()
        {
            Format = ExecutableFormat.PE, FormatName = "PE32+", Architecture = "x86-64", Signed = true,
            Certificates = [new ExecutableCertificate(subject, "CN=Issuer", DateTime.UnixEpoch, DateTime.UnixEpoch, "AB")],
        };

        IReadOnlyList<string[]> rows = ExecutableAnalysisDialog.BuildRows(report);

        int first = rows.ToList().FindIndex(r => r[0] == "Subject");
        string joined = string.Concat(rows.Skip(first).TakeWhile((r, i) => i == 0 || r[0].Length == 0).Select(r => r[1]));
        Assert.AreEqual(subject, joined);
        Assert.IsTrue(rows.All(r => r[1].Length <= ExecutableAnalysisDialog.ValueWidth));
    }

    [TestMethod]
    public void ASignedFileWithNoReadableCertificate_SaysSo()
    {
        ExecutableReport report = new() { Format = ExecutableFormat.PE, FormatName = "PE32+", Architecture = "x86-64", Signed = true };

        IReadOnlyList<string[]> rows = ExecutableAnalysisDialog.BuildRows(report);

        Assert.IsTrue(rows.Any(r => r[1] == "embedded signature, not verified"));
        Assert.IsTrue(rows.Any(r => r[1] == "no certificates could be read"));
    }

    [TestMethod]
    public void ACleanReport_SaysNothingSuspiciousWasFound()
    {
        ExecutableReport report = new() { Format = ExecutableFormat.Elf, FormatName = "ELF", Architecture = "x86-64" };

        IReadOnlyList<string[]> rows = ExecutableAnalysisDialog.BuildRows(report);

        Assert.IsTrue(rows.Any(r => r[1] == "nothing suspicious found"));
    }

    private static ExecutableAnalysisDialog CreateWithListing(FakeAnalyzer analyzer, Func<string, CancellationToken, IReadOnlyList<string>> disassemble) =>
        new(analyzer, disassemble, new ThemeService()) { Url = new Url("file:///usr/bin/sample") };

    /// <summary>Shows the dialog in a window, as the app does, so the children's IsVisible reflects the dialog being up; then starts the read.</summary>
    private static async Task ShowAndInitializeAsync(ExecutableAnalysisDialog dialog)
    {
        Control root = new Window(new ThemeService()) { ViewSize = new Size(100, 30) };
        await root.ShowDialogAsync(dialog);
        await dialog.OnInitializeAsync();
    }

    private static Task PressF5(ExecutableAnalysisDialog dialog) =>
        dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F5, false, false, false));

    private static async Task LandListingAsync(ExecutableAnalysisDialog dialog)
    {
        try
        {
            await dialog.ListingTask!;
        }
        catch
        {
            // A failed build is handled by OnIdle, which is what is under test.
        }

        dialog.OnIdle();
    }

    private static List<string> ListingLines(ExecutableAnalysisDialog dialog) => dialog.Listing.TextModel.Lines.ToList();

    [Timeout(60_000)]
    [TestMethod]
    public async Task F5SwapsTheGridForTheListingAndLandsTheListingOnAnIdlePass()
    {
        var dialog = CreateWithListing(new FakeAnalyzer(), (_, _) => ["  0x0000  push rbp", "  0x0001  mov rbp, rsp"]);
        await ShowAndInitializeAsync(dialog);
        await LandAsync(dialog);

        await PressF5(dialog);

        Assert.IsTrue(dialog.IsShowingListing);
        Assert.IsTrue(dialog.Listing.IsVisible);
        Assert.IsFalse(dialog.Grid.IsVisible);
        await LandListingAsync(dialog);
        CollectionAssert.AreEqual(new[] { "  0x0000  push rbp", "  0x0001  mov rbp, rsp" }, ListingLines(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F5AgainShowsTheGridAgainWithoutBuildingTheListingTwice()
    {
        int builds = 0;
        var dialog = CreateWithListing(new FakeAnalyzer(), (_, _) => { builds++; return ["  0x0000  ret"]; });
        await ShowAndInitializeAsync(dialog);
        await PressF5(dialog);
        await LandListingAsync(dialog);

        await PressF5(dialog);

        Assert.IsFalse(dialog.IsShowingListing);
        Assert.IsTrue(dialog.Grid.IsVisible);
        Assert.IsFalse(dialog.Listing.IsVisible);

        await PressF5(dialog);

        Assert.IsTrue(dialog.IsShowingListing);
        Assert.AreEqual(1, builds, "the listing is kept until the file changes");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheListingIsBuiltOnAWorkerThread()
    {
        int dialogThread = Environment.CurrentManagedThreadId;
        int workerThread = -1;
        var dialog = CreateWithListing(new FakeAnalyzer(), (_, _) =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            return ["  0x0000  ret"];
        });
        await dialog.OnInitializeAsync();

        await PressF5(dialog);
        await dialog.ListingTask!;

        Assert.AreNotEqual(dialogThread, workerThread);
        Assert.IsFalse(ListingLines(dialog).Any(l => l.Contains("ret")), "the result lands on an idle pass, not on the worker");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedListing_SaysWhyInTheListing()
    {
        var dialog = CreateWithListing(new FakeAnalyzer(), (_, _) => throw new InvalidOperationException("boom"));
        await dialog.OnInitializeAsync();
        await PressF5(dialog);
        await LandListingAsync(dialog);

        Assert.IsTrue(ListingLines(dialog).Any(l => l.Contains("boom")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingTheDialogCancelsTheListingBuild()
    {
        CancellationToken seen = default;
        using var started = new ManualResetEventSlim();
        var dialog = CreateWithListing(new FakeAnalyzer(), (_, token) =>
        {
            seen = token;
            started.Set();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            token.ThrowIfCancellationRequested();
            return ["  0x0000  ret"];
        });
        await dialog.OnInitializeAsync();
        await PressF5(dialog);
        started.Wait(TimeSpan.FromSeconds(30));
        Task<IReadOnlyList<string>> task = dialog.ListingTask!;

        dialog.Close();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }

        dialog.OnIdle();

        Assert.IsTrue(seen.IsCancellationRequested);
        Assert.IsNull(dialog.ListingTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningAnotherFile_DropsTheListingOfTheFirst()
    {
        var dialog = CreateWithListing(new FakeAnalyzer(), (_, _) => ["  0x0000  ret"]);
        await ShowAndInitializeAsync(dialog);
        await PressF5(dialog);
        await LandListingAsync(dialog);

        dialog.Url = new Url("file:///usr/bin/other");
        await dialog.OnInitializeAsync();

        Assert.IsFalse(dialog.IsShowingListing);
        Assert.IsTrue(dialog.Grid.IsVisible);
        Assert.AreEqual(0, ListingLines(dialog).Count);
    }
}
