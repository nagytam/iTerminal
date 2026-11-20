using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Test;

/// <summary>
/// The evidence notebook, end to end: Ctrl+Shift+Q asks for the notebook's folder and shows it in the focused
/// panel; Ctrl+Shift+X adds a file from the other panel, or a voice recording from the sandbox's tone microphone.
/// The notebook folder is beside the sandbox panels, so nothing is written outside the sandbox.
/// </summary>
[TestClass]
public class EvidenceNotebookTest
{
    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private string NotebookFolder => Path.Combine(Path.GetDirectoryName(_app.LeftRoot)!, "case-42");

    private string ItemsFolder => Path.Combine(NotebookFolder, "items");

    /// <summary>The app with a file in the right panel, which is where the add command takes files from once the left panel shows the notebook.</summary>
    private async Task StartWithAReportOnTheRightAsync()
    {
        _app = await TerminalApp.StartAsync(seed: (_, right) => File.WriteAllText(Path.Combine(right, "report.txt"), "the report"));
    }

    /// <summary>Opens the notebook in the focused (left) panel, answering its folder prompt with a folder in the sandbox.</summary>
    private async Task OpenNotebookAsync()
    {
        await _app.PressAsync("Ctrl+Shift+Q");
        await _app.WaitForTextAsync("Folder for the evidence notebook");
        await _app.TypeAsync(NotebookFolder);
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(() => _app.FocusedModel is ContentModelEvidenceNotebook, "the focused panel to show the evidence notebook");
        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(NotebookFolder, "notebook.json")), "the notebook's index to be written");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlShiftQ_OpensTheNotebookInTheFolderTyped_AndCtrlShiftXAddsAFileFromTheOtherPanel()
    {
        await StartWithAReportOnTheRightAsync();
        await OpenNotebookAsync();

        await _app.PressAsync("Tab");
        await _app.SelectEntryAsync("report.txt");
        await _app.PressAsync("Ctrl+Shift+X");
        await _app.WaitForTextAsync("Add what to the evidence notebook?");
        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("Added 1 to the notebook");
        var copied = Path.Combine(ItemsFolder, "0001_report.txt");
        Assert.AreEqual("the report", File.ReadAllText(copied), "the bytes are copied, not moved");
        Assert.IsTrue(File.Exists(Path.Combine(_app.RightRoot, "report.txt")), "the source stays where it was");

        var index = File.ReadAllText(Path.Combine(NotebookFolder, "notebook.json"));
        StringAssert.Contains(index, Path.Combine(_app.RightRoot, "report.txt").Replace("\\", "\\\\"), "the origin records the path the file was read from");
        StringAssert.Contains(index, Environment.MachineName, "the origin records the machine");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlShiftX_RecordsAVoiceRecordingAsAWavFile_AndEscDiscardsIt()
    {
        await StartWithAReportOnTheRightAsync();
        await OpenNotebookAsync();

        await _app.PressAsync("Ctrl+Shift+X");
        await _app.WaitForTextAsync("Add what to the evidence notebook?");
        await _app.PressSequenceAsync("Down", "Down", "Enter");
        await _app.WaitForTextAsync("Recording since");
        await _app.PressAsync("Esc");
        await _app.WaitForTextAsync("Recording discarded");
        Assert.AreEqual(0, Directory.GetFiles(ItemsFolder).Length, "a discarded recording is not kept");

        await _app.PressAsync("Ctrl+Shift+X");
        await _app.WaitForTextAsync("Add what to the evidence notebook?");
        await _app.PressSequenceAsync("Down", "Down", "Enter");
        await _app.WaitForTextAsync("Recording since");
        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("Voice recording added");
        var wav = Path.Combine(ItemsFolder, "0001_voice.wav");
        var bytes = File.ReadAllBytes(wav);
        Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.AreEqual("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlShiftX_AddsANote_AndTheNotebookPanelListsIt()
    {
        await StartWithAReportOnTheRightAsync();
        await OpenNotebookAsync();

        await _app.PressAsync("Ctrl+Shift+X");
        await _app.WaitForTextAsync("Add what to the evidence notebook?");
        await _app.PressSequenceAsync("Down", "Enter");
        await _app.WaitForTextAsync("Note");
        await _app.TypeAsync("Door found open at 09:40");
        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("Note added");
        Assert.AreEqual("Door found open at 09:40", File.ReadAllText(Path.Combine(ItemsFolder, "0001_note.txt")));
        await _app.WaitUntilAsync(screen => _app.LeftPanelLists("0001_note.txt", screen), "the notebook panel to list the note");
    }
}
