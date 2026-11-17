using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Viewers;
using iTerminal.Core.Commands;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// The F3 and F4 commands offer the file to the viewer and editor plug-ins in the container before the built-in
/// viewers and the text editor, and fall through to them when no plug-in takes it.
/// </summary>
[TestClass]
public class FilePluginDispatchTest
{
    private sealed class FakeViewer(int priority, string extension, bool confirms = true) : IFileViewer
    {
        public List<Url> Opened { get; } = [];

        public int Priority => priority;

        public bool IsCandidate(Url url) => url.Extension == extension;

        public Task<bool> CanOpenAsync(Url url) => Task.FromResult(confirms);

        public Task OpenAsync(Url url, IFileOpenHost host)
        {
            Opened.Add(url);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEditor(int priority, string extension) : IFileEditor
    {
        public List<Url> Opened { get; } = [];

        public int Priority => priority;

        public bool IsCandidate(Url url) => url.Extension == extension;

        public Task OpenAsync(Url url, IFileOpenHost host)
        {
            Opened.Add(url);
            return Task.CompletedTask;
        }
    }

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static FileViewerDialog CreateTextViewer() => new(
        contentModelService: Mock.Of<IContentModelService>(),
        fileToTextConverterService: Mock.Of<IFileToTextConverterService>(),
        beepService: Mock.Of<IBeepService>(),
        dateTimeProviderService: new DateTimeProviderService(),
        themeService: CreateThemeService());

    private static TextFileEditorDialog CreateTextEditor() => new(
        contentModelService: Mock.Of<IContentModelService>(),
        fileToTextConverterService: Mock.Of<IFileToTextConverterService>(),
        beepService: Mock.Of<IBeepService>(),
        dateTimeProviderService: new DateTimeProviderService(),
        themeService: CreateThemeService());

    /// <summary>A content model service focused on <paramref name="url"/>, whose panel says the file exists.</summary>
    private static Mock<IContentModelService> FocusOn(Url url)
    {
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(url);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(url)).Returns(true);

        var service = new Mock<IContentModelService>();
        service.Setup(s => s.FocusedFileUrl).Returns(url);
        service.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);
        return service;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task View_WhenAViewerPluginTakesTheFile_OpensItAheadOfTheTextViewer()
    {
        var url = new Url("file:///tmp/report.xyz");
        var plugin = new FakeViewer(0, "xyz");
        var registry = new FileViewerRegistry([plugin]);
        var command = new ViewFileDialogCommand(CreateTextViewer(), FocusOn(url).Object, fileViewers: registry);

        await command.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { url.Path }, plugin.Opened.Select(opened => opened.Path).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task View_WhenTheHighestViewerDeclinesAfterLooking_TheNextOneOpensTheFile()
    {
        var url = new Url("file:///tmp/report.xyz");
        var declines = new FakeViewer(10, "xyz", confirms: false);
        var takes = new FakeViewer(0, "xyz");
        var registry = new FileViewerRegistry([declines, takes]);
        var command = new ViewFileDialogCommand(CreateTextViewer(), FocusOn(url).Object, fileViewers: registry);

        await command.ExecuteAsync(null);

        Assert.AreEqual(0, declines.Opened.Count);
        Assert.AreEqual(1, takes.Opened.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task View_WhenNoViewerPluginConfirms_TheTextViewerTakesTheFile()
    {
        var url = new Url("file:///tmp/report.xyz");
        var plugin = new FakeViewer(0, "xyz", confirms: false);
        var registry = new FileViewerRegistry([plugin]);
        var textViewer = CreateTextViewer();
        var command = new ViewFileDialogCommand(textViewer, FocusOn(url).Object, fileViewers: registry);

        await command.ExecuteAsync(null);

        Assert.AreEqual(0, plugin.Opened.Count);
        Assert.AreEqual(url.Path, textViewer.Url?.Path);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void View_CanExecute_IsTrueForAFileOnlyAViewerPluginTakes()
    {
        // .bin is one of the extensions the built-in viewers refuse, so only the plug-in makes F3 available for it.
        var url = new Url("file:///tmp/model.bin");
        var withPlugin = new ViewFileDialogCommand(CreateTextViewer(), FocusOn(url).Object,
            fileViewers: new FileViewerRegistry([new FakeViewer(0, "bin")]));
        var withoutPlugin = new ViewFileDialogCommand(CreateTextViewer(), FocusOn(url).Object,
            fileViewers: new FileViewerRegistry([]));

        Assert.IsTrue(withPlugin.CanExecute());
        Assert.IsFalse(withoutPlugin.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Edit_WhenAnEditorPluginTakesTheFile_OpensItInsteadOfTheTextEditor()
    {
        var url = new Url("file:///tmp/drawing.xyz");
        var plugin = new FakeEditor(0, "xyz");
        var registry = new FileEditorRegistry([plugin]);
        var command = new EditTextFileDialogCommand(CreateTextEditor(), FocusOn(url).Object, null, registry);

        await command.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { url.Path }, plugin.Opened.Select(opened => opened.Path).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Edit_WhenNoEditorPluginTakesTheFile_TheTextEditorIsUsedForATextFile()
    {
        var url = new Url("file:///tmp/notes.txt");
        var plugin = new FakeEditor(0, "xyz");
        var registry = new FileEditorRegistry([plugin]);
        var command = new EditTextFileDialogCommand(CreateTextEditor(), FocusOn(url).Object, null, registry);

        // Without a control the built-in dialog is not shown, so the observable part is that no plug-in was asked to open the file.
        await command.ExecuteAsync(null);

        Assert.AreEqual(0, plugin.Opened.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Edit_CanExecute_IsTrueForAFileOnlyAnEditorPluginTakes()
    {
        // .bin is not in the built-in editor's list, so only the plug-in makes F4 available for it.
        var url = new Url("file:///tmp/model.bin");
        var withPlugin = new EditTextFileDialogCommand(CreateTextEditor(), FocusOn(url).Object, null,
            new FileEditorRegistry([new FakeEditor(0, "bin")]));
        var withoutPlugin = new EditTextFileDialogCommand(CreateTextEditor(), FocusOn(url).Object, null,
            new FileEditorRegistry([]));

        Assert.IsTrue(withPlugin.CanExecute());
        Assert.IsFalse(withoutPlugin.CanExecute());
    }
}
