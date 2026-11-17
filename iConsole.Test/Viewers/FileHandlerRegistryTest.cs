using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Viewers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Viewers;

[TestClass]
public class FileHandlerRegistryTest
{
    private sealed class FakeViewer(int priority, string extension, bool opens = true) : IFileViewer
    {
        public int Priority => priority;

        public bool IsCandidate(Url url) => url.Extension == extension;

        public Task<bool> CanOpenAsync(Url url) => Task.FromResult(opens);

        public Task OpenAsync(Url url, IFileOpenHost host) => Task.CompletedTask;
    }

    private sealed class FakeEditor(int priority, string extension) : IFileEditor
    {
        public int Priority => priority;

        public bool IsCandidate(Url url) => url.Extension == extension;

        public Task OpenAsync(Url url, IFileOpenHost host) => Task.CompletedTask;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Handlers_OrderByPriorityDescending_AndKeepRegistrationOrderWithinAPriority()
    {
        var first = new FakeViewer(0, "txt");
        var high = new FakeViewer(10, "txt");
        var second = new FakeViewer(0, "txt");
        var low = new FakeViewer(-5, "txt");

        var registry = new FileViewerRegistry([first, high, second, low]);

        CollectionAssert.AreEqual(new IFileViewer[] { high, first, second, low }, registry.Handlers.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FirstCandidate_ReturnsTheHighestPriorityHandlerThatTakesTheFile()
    {
        var pdfLow = new FakeEditor(0, "pdf");
        var pdfHigh = new FakeEditor(5, "pdf");
        var registry = new FileEditorRegistry([pdfLow, pdfHigh]);

        Assert.AreSame(pdfHigh, registry.FirstCandidate(new Url("file:///tmp/a.pdf")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FirstCandidate_SkipsHandlersThatDoNotTakeTheFile()
    {
        var md = new FakeViewer(10, "md");
        var txt = new FakeViewer(0, "txt");
        var registry = new FileViewerRegistry([md, txt]);

        Assert.AreSame(txt, registry.FirstCandidate(new Url("file:///tmp/a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FirstCandidate_IsNull_WhenNoHandlerTakesTheFile()
    {
        var registry = new FileViewerRegistry([new FakeViewer(0, "md")]);

        Assert.IsNull(registry.FirstCandidate(new Url("file:///tmp/a.bin")));
        Assert.IsFalse(registry.HasCandidate(new Url("file:///tmp/a.bin")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EmptyRegistry_HasNoCandidates()
    {
        var registry = new FileEditorRegistry([]);

        Assert.AreEqual(0, registry.Handlers.Count);
        Assert.IsFalse(registry.HasCandidate(new Url("file:///tmp/a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileOpenHost_ForwardsDialogsAndTextViewerRequestsToItsDelegates()
    {
        Dialog? shown = null;
        Url? textUrl = null;
        var host = new FileOpenHost(
            dialog =>
            {
                shown = dialog;
                return Task.CompletedTask;
            },
            url =>
            {
                textUrl = url;
                return Task.CompletedTask;
            });
        var target = new Url("file:///tmp/a.cert");

        host.ShowTextViewerAsync(target).GetAwaiter().GetResult();

        Assert.IsNull(shown);
        Assert.AreEqual(target, textUrl);
    }
}
