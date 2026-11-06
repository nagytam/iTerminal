using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Executables;
using iConsole.Services;

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
        new(analyzer, new ThemeService()) { Url = new Url("file:///usr/bin/sample") };

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
        var dialog = new ExecutableAnalysisDialog(analyzer, new ThemeService());

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
}
