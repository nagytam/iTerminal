using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Printing;

namespace iTerminal.Test;

/// <summary>Ctrl+P, end to end: print the file under the cursor and manage the queue, on the sandbox's in-memory printers.</summary>
[TestClass]
public class PrintingTest
{
    private TerminalApp _app = null!;

    private SandboxServices.Printing Printing => _app.Driver.GetRequiredService<SandboxServices.Printing>();

    [TestCleanup]
    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private string ReadmePath => Path.Combine(_app.LeftRoot, "readme.txt");

    [TestMethod]
    [Timeout(45_000)] // A hang guard: each test boots the whole app.
    public async Task CtrlP_PrintsTheFile_ThenHoldsAndCancelsTheJob()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+P");
        await _app.Driver.WaitForModalAsync<PrintDialog>();
        await _app.WaitForTextAsync("readme.txt");
        await _app.WaitForTextAsync(SandboxServices.Printing.PrinterNames[0]);

        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("readme.txt was sent to " + SandboxServices.Printing.PrinterNames[0]);
        await _app.WaitForTextAsync("Queued");
        CollectionAssert.AreEqual(new[] { $"print {SandboxServices.Printing.PrinterNames[0]} {ReadmePath} 1" }, Printing.Calls.ToArray());

        await _app.PressAsync("P");
        await _app.WaitForTextAsync("Held");
        Assert.AreEqual(PrintJobState.Held, Printing.Jobs.Single().State);

        await _app.PressAsync("Delete");
        await _app.WaitForTextAsync("y cancels the job");
        await _app.PressAsync("Y");
        await _app.WaitForTextAsync("The queue is empty.");
        Assert.AreEqual(0, Printing.Jobs.Count);

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
        Assert.IsTrue(File.Exists(ReadmePath), "printing never touches the file");
    }

    [TestMethod]
    [Timeout(45_000)] // A hang guard: each test boots the whole app.
    public async Task CtrlP_OnAFolder_OpensTheQueueAlone()
    {
        _app = await TerminalApp.StartAsync();

        await _app.PressAsync("Ctrl+P");
        await _app.Driver.WaitForModalAsync<PrintDialog>();
        await _app.WaitForTextAsync("The queue is empty.");

        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("no file under the cursor");
        Assert.AreEqual(0, Printing.Calls.Count);
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
    }
}
