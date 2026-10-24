using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Ctrl+F10's dialog: reading two files, showing them side by side, and moving between the differences.</summary>
[TestClass]
public class FileDiffDialogTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "FileDiffDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Url Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return new Url(path);
    }

    private static async Task<(FileDiffDialog Dialog, iConsole.Controls.Control Root)> ShowAsync(Url left, Url right)
    {
        var dialog = new FileDiffDialog(UiTestHelper.TestTheme());
        dialog.Load(left, null, right, null);
        var root = await UiTestHelper.ShowAsync(dialog);
        await dialog.LoadTask;
        return (dialog, root);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ShowsTheTwoFilesAndHowTheyDiffer()
    {
        var left = Write("a.txt", "one\ntwo\nthree\n");
        var right = Write("b.txt", "one\n2\nthree\nfour\n");

        var (dialog, root) = await ShowAsync(left, right);

        Assert.IsNotNull(dialog.Result);
        Assert.AreEqual(2, dialog.Result.HunkStarts.Count);
        var screen = UiTestHelper.Render(root);
        UiTestHelper.AssertScreenContains(screen, "a.txt");
        UiTestHelper.AssertScreenContains(screen, "b.txt");
        UiTestHelper.AssertScreenContains(screen, "two");
        UiTestHelper.AssertScreenContains(screen, "four");
        UiTestHelper.AssertScreenContains(screen, "2 differences");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task IdenticalFilesSaySo()
    {
        var left = Write("a.txt", "same\r\nlines\r\n");
        var right = Write("b.txt", "same\nlines\n");

        var (dialog, root) = await ShowAsync(left, right);

        Assert.IsTrue(dialog.Result!.AreIdentical);
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "identical");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task NAndPMoveBetweenDifferences()
    {
        var lines = Enumerable.Range(1, 200).Select(i => "line " + i).ToArray();
        var changed = (string[])lines.Clone();
        changed[49] = "CHANGED 50";
        changed[149] = "CHANGED 150";
        var left = Write("a.txt", string.Join('\n', lines));
        var right = Write("b.txt", string.Join('\n', changed));

        var (dialog, root) = await ShowAsync(left, right);
        var first = dialog.TopRow;
        Assert.IsTrue(first is > 40 and < 50, $"opens on the first difference, not at {first}");

        await dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.N));
        var second = dialog.TopRow;
        Assert.IsTrue(second > 140, $"N goes to the second difference, not {second}");

        await dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.P));
        Assert.AreEqual(first, dialog.TopRow);
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "CHANGED 50");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task WhitespaceCanBeIgnoredWithW()
    {
        var left = Write("a.txt", "a  b\nc\n");
        var right = Write("b.txt", "a b\nc\n");

        var (dialog, _) = await ShowAsync(left, right);
        Assert.IsFalse(dialog.Result!.AreIdentical);

        await dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.W, 'w'));
        await dialog.LoadTask;

        Assert.IsTrue(dialog.IgnoreWhitespace);
        Assert.IsTrue(dialog.Result!.AreIdentical);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task BinaryFilesAreComparedByTheirBytes()
    {
        var left = new Url(Path.Combine(_folder, "a.bin"));
        var right = new Url(Path.Combine(_folder, "b.bin"));
        File.WriteAllBytes(left.Path, [1, 2, 0, 3]);
        File.WriteAllBytes(right.Path, [1, 2, 0, 4]);

        var (dialog, _) = await ShowAsync(left, right);

        Assert.IsNull(dialog.Result);
        StringAssert.Contains(dialog.Status, "Binary files");
        StringAssert.Contains(dialog.Status, "differ");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task AMissingFileIsReportedNotThrown()
    {
        var left = Write("a.txt", "x");
        var right = new Url(Path.Combine(_folder, "missing.txt"));

        var (dialog, _) = await ShowAsync(left, right);

        Assert.IsNull(dialog.Result);
        StringAssert.Contains(dialog.Status, "Could not read");
    }

    [TestMethod]
    public void LinesSplitOnEveryKindOfBreakAndKeepNoPhantomLastLine()
    {
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, FileDiffDialog.SplitLines("a\r\nb\nc\rd\n"));
        CollectionAssert.AreEqual(new[] { "a", string.Empty, "b" }, FileDiffDialog.SplitLines("a\n\nb"));
        Assert.AreEqual(0, FileDiffDialog.SplitLines(string.Empty).Length);
    }

    [TestMethod]
    public void TextThatIsNotUtf8IsReadAsLatin1NotWithReplacementCharacters()
    {
        Assert.AreEqual("café", FileDiffDialog.Decode([0x63, 0x61, 0x66, 0xE9]));
        Assert.AreEqual("café", FileDiffDialog.Decode([0xEF, 0xBB, 0xBF, 0x63, 0x61, 0x66, 0xC3, 0xA9]));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task TheWheelScrollsTheComparisonThreeLinesAtATime_AndStopsAtTheEnds()
    {
        string many = string.Join("\n", Enumerable.Range(0, 120).Select(i => "line" + i.ToString("000"))) + "\n";
        var (dialog, root) = await ShowAsync(Write("a.txt", many), Write("b.txt", many + "extra\n"));
        Position inside = new(dialog.AbsolutePosition.X + 10, dialog.AbsolutePosition.Y + 8);

        // The view opens on the first difference, which here is the last line.
        await dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Home));
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "line000");
        UiTestHelper.AssertScreenDoesNotContain(UiTestHelper.Render(root), "line100");

        await dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.End));
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "line119");

        await dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Home));
        Assert.IsTrue(await dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = 1 }));
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "line000");

        await dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = -1 });
        UiTestHelper.AssertScreenDoesNotContain(UiTestHelper.Render(root), "line000");
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "line003");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task TheWheelOutsideTheDialog_IsNotTheDialogs()
    {
        var (dialog, _) = await ShowAsync(Write("a.txt", "x\n"), Write("b.txt", "y\n"));

        Assert.IsFalse(await dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = -1 }));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ABinaryFileOpensInHex_WithItsDifferingBytesCounted()
    {
        var left = new Url(Path.Combine(_folder, "a.bin"));
        var right = new Url(Path.Combine(_folder, "b.bin"));
        File.WriteAllBytes(left.Path, [1, 2, 0, 3]);
        File.WriteAllBytes(right.Path, [1, 9, 0, 4]);

        var (dialog, root) = await ShowAsync(left, right);
        dialog.OnIdle();

        Assert.IsTrue(dialog.IsHexMode);
        Assert.AreEqual(2, dialog.Binary!.DifferingBytes);
        StringAssert.Contains(dialog.Status, "2 bytes in 2 place(s)");
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "01 02 00 03");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task F4_ShowsATextFilesBytes_AndBackToItsLines()
    {
        var left = Write("a.txt", "one\ntwo\n");
        var right = Write("b.txt", "one\n2\n");
        var (dialog, root) = await ShowAsync(left, right);
        Assert.IsFalse(dialog.IsHexMode);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F4, false, false, false));
        Assert.IsTrue(dialog.IsHexMode);
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "6F 6E 65");

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F4, false, false, false));
        Assert.IsFalse(dialog.IsHexMode);
        Assert.IsNotNull(dialog.Result);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task NAndP_MoveBetweenTheRunsOfDifferingBytes_InHex()
    {
        // Two single-byte differences in a text of about a hundred lines, far enough apart to be on different rows.
        var line = new string('x', 40);
        var left = Write("a.txt", string.Join("\n", Enumerable.Repeat(line, 100)) + "\n");
        var changed = Enumerable.Repeat(line, 100).ToArray();
        changed[0] = "y" + line[1..];
        changed[90] = "y" + line[1..];
        var right = Write("b.txt", string.Join("\n", changed) + "\n");
        var (dialog, root) = await ShowAsync(left, right);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F4, false, false, false));
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "(1/2)");

        await dialog.HandleKeyEventAsync(new KeyEvent('n', ConsoleKey.N, false, false, false));
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "(2/2)");

        await dialog.HandleKeyEventAsync(new KeyEvent('p', ConsoleKey.P, false, false, false));
        UiTestHelper.AssertScreenContains(UiTestHelper.Render(root), "(1/2)");
    }
}
