using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Streams;

namespace iTerminal.Test;

/// <summary>Streams and attributes of the entry under the cursor, end to end, on the sandbox's in-memory streams.</summary>
[TestClass]
public class StreamsTest
{
    private TerminalApp _app = null!;

    private SandboxServices.StreamAttributes Streams => _app.Driver.GetRequiredService<SandboxServices.StreamAttributes>();

    private string ReadmePath => Path.Combine(_app.LeftRoot, "readme.txt");

    [TestCleanup]
    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task StartWithStreamsAsync()
    {
        _app = await TerminalApp.StartAsync();
        Assert.IsInstanceOfType<SandboxServices.StreamAttributes>(_app.Driver.GetRequiredService<IStreamAttributeService>(), "the real service must never be reachable from a test");
        Streams.Set(ReadmePath, "Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");
        Streams.Set(ReadmePath, "notes", "second stream");
        await _app.SelectEntryAsync("readme.txt");
    }

    private async Task OpenFromTheFileMenuAsync()
    {
        await _app.PressAsync("Alt+F");
        await _app.WaitForTextAsync("Streams and Attributes");
        await _app.Driver.ClickAsync("Streams and Attributes");
        await _app.Driver.WaitForModalAsync<StreamsDialog>();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task FromTheFileMenu_ListsAndPreviewsTheStreams()
    {
        await StartWithStreamsAsync();

        await OpenFromTheFileMenuAsync();

        await _app.WaitForTextAsync("Zone.Identifier");
        await _app.WaitForTextAsync("notes");
        await _app.WaitForTextAsync("ZoneId=3");
        await _app.PressAsync("Down");
        await _app.WaitForTextAsync("second stream");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
        CollectionAssert.AreEquivalent(new[] { "Zone.Identifier", "notes" }, Streams.NamesOf(ReadmePath).ToArray());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Delete_AsksFirst_AnyOtherKeyKeeps_YDeletes()
    {
        await StartWithStreamsAsync();
        await OpenFromTheFileMenuAsync();
        await _app.WaitForTextAsync("ZoneId=3");

        await _app.PressAsync("Delete");
        await _app.WaitForTextAsync("y deletes it");
        await _app.PressAsync("N");
        await _app.WaitForTextGoneAsync("y deletes it");
        Assert.AreEqual(2, Streams.NamesOf(ReadmePath).Count, "an answer other than y keeps it");
        Assert.IsFalse(Streams.Calls.Any(c => c.StartsWith("delete")));

        await _app.PressAsync("Delete");
        await _app.WaitForTextAsync("Delete Zone.Identifier? y deletes it.");
        await _app.PressAsync("Y");
        await _app.WaitForTextAsync("Deleted Zone.Identifier.");

        CollectionAssert.AreEqual(new[] { "notes" }, Streams.NamesOf(ReadmePath).ToArray());
        await _app.WaitForTextGoneAsync("ZoneId=3");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        Assert.IsTrue(File.Exists(ReadmePath), "the file's own contents are never touched");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task FromTheCommandPalette_OnAnEntryWithNoStreams_SaysSo()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Shift+F1");
        await _app.Driver.WaitForModalAsync<CommandPaletteDialog>();
        await _app.TypeAsync("streams and");
        await _app.WaitForTextAsync("Streams and Attributes");
        await _app.PressAsync("Enter");

        await _app.Driver.WaitForModalAsync<StreamsDialog>();
        await _app.WaitForTextAsync("has no streams");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task OnTheParentRow_TheMenuEntryDoesNothing()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("..");

        await _app.PressAsync("Shift+F1");
        await _app.Driver.WaitForModalAsync<CommandPaletteDialog>();
        await _app.TypeAsync("streams and");
        await Task.Delay(300);

        Assert.IsFalse((await _app.WaitForTextAsync("Enter:run")).Contains("Streams and Attributes"), "never offered on the .. row");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        Assert.AreEqual(0, Streams.Calls.Count);
    }
}
