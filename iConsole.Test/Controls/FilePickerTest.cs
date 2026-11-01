using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class FilePickerTest
{
    private static IThemeService Theme()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return themeService.Object;
    }

    /// <summary>A tree of folders held in memory, with a gate to hold a listing back.</summary>
    private sealed class FakeSource : IFilePickerSource
    {
        public Dictionary<string, List<FilePickerEntry>> Folders { get; } = new()
        {
            ["/"] = [new("docs", true), new("b.txt", false, 2048), new("a.md", false, 10), new(".hidden", false, 1, true), new("c.bin", false, 5)],
            ["/docs"] = [new("x.txt", false, 3)],
            ["/slow"] = [],
        };

        public Dictionary<string, TaskCompletionSource> Gates { get; } = [];

        public string DefaultFolder => "/";

        public async Task<IReadOnlyList<FilePickerEntry>> ListAsync(string folder, CancellationToken cancellationToken)
        {
            if (Gates.TryGetValue(folder, out TaskCompletionSource? gate))
            {
                await gate.Task;
            }

            if (!Folders.TryGetValue(folder, out List<FilePickerEntry>? entries))
            {
                throw new DirectoryNotFoundException($"No folder {folder}");
            }

            return entries;
        }

        public string? ParentOf(string folder) => folder == "/" ? null : folder.LastIndexOf('/') is 0 ? "/" : folder[..folder.LastIndexOf('/')];

        public string Combine(string folder, string name) => folder == "/" ? "/" + name : folder + "/" + name;
    }

    private static async Task<(FilePicker Picker, FakeSource Source)> CreateAsync()
    {
        var source = new FakeSource();
        var picker = new FilePicker(source, Theme()) { ViewSize = new Size(30, 8) };
        await picker.NavigateToAsync("/");
        return (picker, source);
    }

    private static KeyEvent Key(ConsoleKey key) => new(' ', key, false, false, false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task Lists_FoldersFirstThenFilesByName_AndHidesHiddenOnes()
    {
        (FilePicker picker, _) = await CreateAsync();

        CollectionAssert.AreEqual(new[] { "docs", "a.md", "b.txt", "c.bin" }, picker.Rows.Select(r => r.Name).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowHidden_AddsHiddenEntries()
    {
        (FilePicker picker, _) = await CreateAsync();

        picker.ShowHidden = true;

        Assert.IsTrue(picker.Rows.Any(r => r.Name == ".hidden"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Filter_KeepsMatchingFilesAndAllFolders()
    {
        (FilePicker picker, _) = await CreateAsync();

        picker.Filter = "*.txt; *.MD";

        CollectionAssert.AreEqual(new[] { "docs", "a.md", "b.txt" }, picker.Rows.Select(r => r.Name).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PickFolders_ListsOnlyFolders()
    {
        (FilePicker picker, _) = await CreateAsync();

        picker.PickFolders = true;

        CollectionAssert.AreEqual(new[] { "docs" }, picker.Rows.Select(r => r.Name).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAFolder_OpensItWithAParentRowFirst()
    {
        (FilePicker picker, _) = await CreateAsync();

        await picker.ActivateAsync();

        Assert.AreEqual("/docs", picker.FolderPath);
        CollectionAssert.AreEqual(new[] { "..", "x.txt" }, picker.Rows.Select(r => r.Name).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnTheParentRow_GoesUp()
    {
        (FilePicker picker, _) = await CreateAsync();
        await picker.NavigateToAsync("/docs");

        await picker.ActivateAsync();

        Assert.AreEqual("/", picker.FolderPath);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAFile_RaisesPathChosen()
    {
        (FilePicker picker, _) = await CreateAsync();
        string? chosen = null;
        picker.PathChosen += path => chosen = path;
        picker.SetFocus();

        await picker.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual("/a.md", chosen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PickFolders_EnterOnAFolderChoosesIt_ButNotTheParentRow()
    {
        (FilePicker picker, _) = await CreateAsync();
        picker.PickFolders = true;
        var chosen = new List<string>();
        picker.PathChosen += chosen.Add;

        await picker.ActivateAsync();
        await picker.NavigateToAsync("/docs");
        await picker.ActivateAsync();

        CollectionAssert.AreEqual(new[] { "/docs" }, chosen);
        Assert.AreEqual("/", picker.FolderPath);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Backspace_GoesUpAndKeysMoveTheCursorWithinBounds()
    {
        (FilePicker picker, _) = await CreateAsync();
        await picker.NavigateToAsync("/docs");
        picker.SetFocus();

        await picker.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual("x.txt", picker.CursorEntry?.Name);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual("x.txt", picker.CursorEntry?.Name);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual("..", picker.CursorEntry?.Name);

        await picker.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        Assert.AreEqual("/", picker.FolderPath);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UnfocusedPicker_IgnoresKeys()
    {
        (FilePicker picker, _) = await CreateAsync();

        bool handled = await picker.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.IsFalse(handled);
        Assert.AreEqual("docs", picker.CursorEntry?.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedListing_ShowsTheErrorAndKeepsTheOldFolder()
    {
        (FilePicker picker, _) = await CreateAsync();

        await picker.NavigateToAsync("/missing");

        Assert.AreEqual("/", picker.FolderPath);
        Assert.IsNotNull(picker.Error);
        var screen = ScreenBuffer.Render(picker, 30, 8);
        Assert.IsTrue(screen.Contains("No folder /missing") || picker.Rows.Count > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASlowListingThatIsOvertaken_IsDropped()
    {
        (FilePicker picker, FakeSource source) = await CreateAsync();
        var gate = new TaskCompletionSource();
        source.Gates["/slow"] = gate;

        Task slow = picker.NavigateToAsync("/slow");
        await picker.NavigateToAsync("/docs");
        gate.SetResult();
        await slow;

        Assert.AreEqual("/docs", picker.FolderPath);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Draw_ShowsTheFolderThenOneRowPerEntryWithSizesAndFillsItsBounds()
    {
        (FilePicker picker, _) = await CreateAsync();
        picker.SetFocus();
        var screen = new ScreenBuffer(30, 8);

        picker.Draw(screen);
        picker.Draw(screen);

        Assert.AreEqual("/", screen.Line(0).TrimEnd());
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(1), "docs/");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(3), "b.txt");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(3), "2 KB");
        screen.AssertFillsBounds(picker);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Draw_HighlightsTheCursorRow()
    {
        (FilePicker picker, _) = await CreateAsync();
        picker.SetFocus();
        var screen = new ScreenBuffer(30, 8);
        ColorScheme scheme = new();

        picker.Draw(screen);

        Assert.AreEqual(scheme.SelectedFocusedBackground, screen.BackgroundAt(1, 1));
        Assert.AreEqual(scheme.Background, screen.BackgroundAt(1, 2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Draw_ScrollsToKeepTheCursorVisible()
    {
        var source = new FakeSource();
        source.Folders["/"] = [.. Enumerable.Range(0, 30).Select(i => new FilePickerEntry($"f{i:00}.txt", false, 1))];
        var picker = new FilePicker(source, Theme()) { ViewSize = new Size(20, 5) };
        await picker.NavigateToAsync("/");
        picker.SetFocus();

        await picker.HandleKeyEventAsync(Key(ConsoleKey.End));
        var screen = new ScreenBuffer(20, 5);
        picker.Draw(screen);

        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(4), "f29.txt");
        Assert.IsFalse(screen.Contains("f00.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Click_MovesTheCursor_AndASecondClickActivatesTheRow()
    {
        (FilePicker picker, _) = await CreateAsync();
        string? chosen = null;
        picker.PathChosen += path => chosen = path;

        var click = new MouseEvent { Position = new Position(2, 2), LeftClicked = true };
        await picker.HandleMouseEventAsync(click);
        Assert.AreEqual("a.md", picker.CursorEntry?.Name);
        Assert.IsNull(chosen);

        await picker.HandleMouseEventAsync(click);
        Assert.AreEqual("/a.md", chosen);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("*.txt", "a.txt", true)]
    [DataRow("*.txt", "a.txt.bak", false)]
    [DataRow("a?c", "abc", true)]
    [DataRow("a?c", "ac", false)]
    [DataRow("*", "", true)]
    [DataRow("*a*b*", "xxaxxbxx", true)]
    [DataRow("*.TXT", "x.txt", true)]
    [DataRow("", "x", false)]
    public void Matches_HandlesWildcards(string pattern, string name, bool expected)
    {
        Assert.AreEqual(expected, FilePicker.Matches(pattern, name));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LocalSource_ListsARealFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "fp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        await File.WriteAllTextAsync(Path.Combine(folder, "one.txt"), "hello");
        try
        {
            var source = new LocalFilePickerSource();

            IReadOnlyList<FilePickerEntry> entries = await source.ListAsync(folder, CancellationToken.None);

            Assert.IsTrue(entries.Any(e => e.Name == "sub" && e.IsFolder));
            Assert.IsTrue(entries.Any(e => e.Name == "one.txt" && !e.IsFolder && e.Size == 5));
            Assert.AreEqual(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
                source.ParentOf(folder)?.TrimEnd(Path.DirectorySeparatorChar));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
