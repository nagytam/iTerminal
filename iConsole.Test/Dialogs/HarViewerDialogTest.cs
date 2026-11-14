using System.Text;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class HarViewerDialogTest
{
    private const string TwoRequests = """
        {"log":{"entries":[
          {"startedDateTime":"2026-11-14T09:30:00.000+00:00","time":89.7,
           "request":{"method":"GET","url":"https://example.com/"},
           "response":{"status":200,"statusText":"OK","content":{"size":144,"mimeType":"text/html; charset=utf-8","text":"<h1>Hi</h1>"}}},
          {"startedDateTime":"2026-11-14T09:30:00.120+00:00","time":33.0,
           "request":{"method":"POST","url":"https://example.com/api/login"},
           "response":{"status":401,"statusText":"Unauthorized","content":{"size":33,"mimeType":"application/json","text":"{\"error\":\"invalid\"}"}}}
        ]}}
        """;

    private static HarViewerDialog CreateDialog(string path, string text)
    {
        var document = new Url(path);
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(Encoding.UTF8.GetBytes(text));
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(document)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        return new HarViewerDialog(models.Object, Mock.Of<IFileToTextConverterService>(), new ThemeService()) { Url = document };
    }

    private static KeyEvent Key(ConsoleKey key) => new('\0', key, false, false, false);

    [TestMethod]
    [Timeout(60_000)]
    public async Task OpensWithOneRowPerRequestAndTheCount()
    {
        HarViewerDialog dialog = CreateDialog("file:///data/example.har", TwoRequests);

        await dialog.OnInitializeAsync();

        Assert.AreEqual(2, dialog.Grid.RowCount);
        Assert.AreEqual(7, dialog.Grid.ColumnCount);
        Assert.AreEqual("2 request(s)", dialog.Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task EachRowShowsMethodStatusTypeSizeTimeAndUrl()
    {
        HarViewerDialog dialog = CreateDialog("file:///data/example.har", TwoRequests);

        await dialog.OnInitializeAsync();

        Assert.AreEqual("1", dialog.Grid.CellAt(0, 0));
        Assert.AreEqual("GET", dialog.Grid.CellAt(0, 1));
        Assert.AreEqual("200", dialog.Grid.CellAt(0, 2));
        Assert.AreEqual("text/html", dialog.Grid.CellAt(0, 3), "the type drops the charset parameter");
        Assert.AreEqual("144", dialog.Grid.CellAt(0, 4));
        Assert.AreEqual("https://example.com/", dialog.Grid.CellAt(0, 6));
        Assert.AreEqual("POST", dialog.Grid.CellAt(1, 1));
        Assert.AreEqual("401", dialog.Grid.CellAt(1, 2));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AFileThatIsNotHarSaysSoAndListsNothing()
    {
        HarViewerDialog dialog = CreateDialog("file:///data/notes.har", """{"name":"not a log"}""");

        await dialog.OnInitializeAsync();

        Assert.AreEqual(0, dialog.Grid.RowCount);
        StringAssert.StartsWith(dialog.Status, "Not a HAR file:");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ALogWithNoRequestsSaysSo()
    {
        HarViewerDialog dialog = CreateDialog("file:///data/empty.har", """{"log":{"entries":[]}}""");

        await dialog.OnInitializeAsync();

        Assert.AreEqual(0, dialog.Grid.RowCount);
        Assert.AreEqual("The file has no requests", dialog.Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task EnterOpensTheRequestAndItsResponse()
    {
        HarViewerDialog dialog = CreateDialog("file:///data/example.har", TwoRequests);
        await dialog.OnInitializeAsync();

        bool handled = await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        Assert.IsTrue(dialog.IsShowingDetails);
        Assert.AreEqual("GET https://example.com/", dialog.DetailLines[0]);
        CollectionAssert.Contains(dialog.DetailLines, "  <h1>Hi</h1>");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task BackspaceReturnsFromTheDetailsToTheList()
    {
        HarViewerDialog dialog = CreateDialog("file:///data/example.har", TwoRequests);
        await dialog.OnInitializeAsync();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        bool handled = await dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));

        Assert.IsTrue(handled);
        Assert.IsFalse(dialog.IsShowingDetails);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task F4ShowsTheFilesTextAndAgainTheList()
    {
        HarViewerDialog dialog = CreateDialog("file:///data/example.har", TwoRequests);
        await dialog.OnInitializeAsync();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));
        Assert.IsTrue(dialog.IsTextMode);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));
        Assert.IsFalse(dialog.IsTextMode);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task EnterDoesNothingWhileTheFilesTextIsShown()
    {
        HarViewerDialog dialog = CreateDialog("file:///data/example.har", TwoRequests);
        await dialog.OnInitializeAsync();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsFalse(dialog.IsShowingDetails);
        Assert.IsTrue(dialog.IsTextMode);
    }
}
