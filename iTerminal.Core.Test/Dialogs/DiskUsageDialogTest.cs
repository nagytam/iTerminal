using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.DiskUsage;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Ctrl+Shift+F12: where the space went — scanning, moving around the tree, sorting, rescanning and jumping to the panel.</summary>
[TestClass]
public class DiskUsageDialogTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iterminal-dudialog-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_folder, "big"));
        Directory.CreateDirectory(Path.Combine(_folder, "small"));
        File.WriteAllBytes(Path.Combine(_folder, "big", "a.bin"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(_folder, "big", "b.bin"), new byte[3000]);
        File.WriteAllBytes(Path.Combine(_folder, "small", "c.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_folder, "top.txt"), new byte[10]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class NeverFinishingService : IDiskUsageService
    {
        public DiskUsageScan Start(string path) => new(path);
    }

    private sealed record Fixture(DiskUsageDialog Dialog, Control Root, Mock<IContentModelService> Panels);

    private async Task<Fixture> OpenAsync(string? path = null, IDiskUsageService? service = null, bool waitForScan = true)
    {
        Mock<IContentModelService> panels = new();
        panels.Setup(p => p.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>())).Returns(Task.CompletedTask);
        panels.Setup(p => p.SetUrlAsync(It.IsAny<Url>())).Returns(Task.CompletedTask);
        DiskUsageDialog dialog = new(service ?? new DiskUsageService(), TestTheme(), panels.Object);
        dialog.Load(path ?? _folder, "Left");
        Control root = await ShowAsync(dialog);
        if (waitForScan)
        {
            await dialog.Scan!.Completion;
            await WaitUntilAsync(() =>
            {
                dialog.OnIdle();
                return dialog.Current is not null;
            }, "the finished scan to be shown");
        }

        return new Fixture(dialog, root, panels);
    }

    private static Task PressAsync(Fixture f, ConsoleKey key, char c = '\0') => f.Dialog.HandleKeyEventAsync(Key(key, c));

    [TestMethod]
    public async Task WhileScanning_ItSaysSoAndTheBrowsingKeysDoNothing()
    {
        Fixture f = await OpenAsync(service: new NeverFinishingService(), waitForScan: false);

        AssertScreenContains(Render(f.Root), "Measuring");
        AssertScreenContains(Render(f.Root), "Esc stops and closes.");
        Assert.IsNull(f.Dialog.Current);
        Assert.IsNull(f.Dialog.Selected);
        await PressAsync(f, ConsoleKey.S, 's');
        await PressAsync(f, ConsoleKey.Enter);
        await PressAsync(f, ConsoleKey.End);
        AssertScreenContains(Render(f.Root), "Measuring");
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task WhenDone_ItListsTheLevelLargestFirst_WithSizesAndShares()
    {
        Fixture f = await OpenAsync();

        AssertScreenContains(Render(f.Root), "Disk usage");
        AssertScreenContains(Render(f.Root), _folder);
        AssertScreenContains(Render(f.Root), "4 files, 2 folders");
        AssertScreenContains(Render(f.Root), "big/");
        AssertScreenContains(Render(f.Root), "small/");
        AssertScreenContains(Render(f.Root), "top.txt");
        AssertScreenContains(Render(f.Root), "BY SIZE");
        Assert.AreEqual("big", f.Dialog.Selected!.Name);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task TheArrowsHomeEndAndPages_MoveTheCursorAndStayInTheList()
    {
        Fixture f = await OpenAsync();

        await PressAsync(f, ConsoleKey.DownArrow);
        Assert.AreEqual("small", f.Dialog.Selected!.Name);
        await PressAsync(f, ConsoleKey.End);
        Assert.AreEqual("top.txt", f.Dialog.Selected!.Name);
        await PressAsync(f, ConsoleKey.DownArrow);
        Assert.AreEqual("top.txt", f.Dialog.Selected!.Name);
        await PressAsync(f, ConsoleKey.Home);
        Assert.AreEqual("big", f.Dialog.Selected!.Name);
        await PressAsync(f, ConsoleKey.UpArrow);
        Assert.AreEqual("big", f.Dialog.Selected!.Name);
        await PressAsync(f, ConsoleKey.PageDown);
        Assert.AreEqual("top.txt", f.Dialog.Selected!.Name);
        await PressAsync(f, ConsoleKey.PageUp);
        Assert.AreEqual("big", f.Dialog.Selected!.Name);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task Enter_GoesIntoAFolder_AndBackspaceComesOutWithTheCursorOnIt()
    {
        Fixture f = await OpenAsync();

        await PressAsync(f, ConsoleKey.DownArrow);
        await PressAsync(f, ConsoleKey.Enter);
        Assert.AreEqual("small", f.Dialog.Current!.Name);
        AssertScreenContains(Render(f.Root), "c.bin");
        AssertScreenContains(Render(f.Root), "of ");

        await PressAsync(f, ConsoleKey.Backspace);
        Assert.AreEqual(Path.GetFileName(_folder), f.Dialog.Current!.Name);
        Assert.AreEqual("small", f.Dialog.Selected!.Name);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task RightAndLeft_DoWhatEnterAndBackspaceDo()
    {
        Fixture f = await OpenAsync();

        await PressAsync(f, ConsoleKey.RightArrow);
        Assert.AreEqual("big", f.Dialog.Current!.Name);
        await PressAsync(f, ConsoleKey.LeftArrow);
        Assert.AreEqual(Path.GetFileName(_folder), f.Dialog.Current!.Name);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task EnterOnAFile_AndBackspaceAtTheTop_DoNothing()
    {
        Fixture f = await OpenAsync();

        await PressAsync(f, ConsoleKey.End);
        await PressAsync(f, ConsoleKey.Enter);
        Assert.AreEqual(Path.GetFileName(_folder), f.Dialog.Current!.Name);
        await PressAsync(f, ConsoleKey.Backspace);
        Assert.AreEqual(Path.GetFileName(_folder), f.Dialog.Current!.Name);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task S_SortsByNameFoldersFirst_AndAgainBySize()
    {
        Fixture f = await OpenAsync();

        await PressAsync(f, ConsoleKey.S, 's');
        AssertScreenContains(Render(f.Root), "BY NAME");
        Assert.AreEqual("big", f.Dialog.Selected!.Name);
        await PressAsync(f, ConsoleKey.DownArrow);
        Assert.AreEqual("small", f.Dialog.Selected!.Name);
        await PressAsync(f, ConsoleKey.DownArrow);
        Assert.AreEqual("top.txt", f.Dialog.Selected!.Name);

        await PressAsync(f, ConsoleKey.S, 's');
        AssertScreenContains(Render(f.Root), "BY SIZE");
        Assert.AreEqual(0, new[] { "big", "small", "top.txt" }.ToList().IndexOf(f.Dialog.Selected!.Name));
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task R_MeasuresAgain_AndSeesWhatChanged()
    {
        Fixture f = await OpenAsync();
        DiskUsageScan first = f.Dialog.Scan!;
        File.WriteAllBytes(Path.Combine(_folder, "late.bin"), new byte[20000]);

        await PressAsync(f, ConsoleKey.R, 'r');
        Assert.AreNotSame(first, f.Dialog.Scan);
        await f.Dialog.Scan!.Completion;
        await WaitUntilAsync(() =>
        {
            f.Dialog.OnIdle();
            return f.Dialog.Current is not null;
        }, "the rescan to be shown");

        Assert.AreEqual("late.bin", f.Dialog.Selected!.Name);
        AssertScreenContains(Render(f.Root), "5 files");
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task G_PointsThePanelAtTheFolderUnderTheCursor_AndCloses()
    {
        Fixture f = await OpenAsync();

        await PressAsync(f, ConsoleKey.G, 'g');

        f.Panels.Verify(p => p.SetUrlAsync("Left", It.Is<Url>(u => u.FullUrl.Contains("big"))), Times.Once);
        Assert.IsFalse(f.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task G_OnAFile_GoesToTheFolderShown()
    {
        Fixture f = await OpenAsync();

        await PressAsync(f, ConsoleKey.End);
        await PressAsync(f, ConsoleKey.G, 'g');

        f.Panels.Verify(p => p.SetUrlAsync("Left", It.Is<Url>(u => u.FullUrl.Contains(Path.GetFileName(_folder)))), Times.Once);
    }

    [TestMethod]
    public async Task G_WithoutAPanelName_UsesTheFocusedPanel()
    {
        Mock<IContentModelService> panels = new();
        panels.Setup(p => p.SetUrlAsync(It.IsAny<Url>())).Returns(Task.CompletedTask);
        DiskUsageDialog dialog = new(new DiskUsageService(), TestTheme(), panels.Object);
        dialog.Load(_folder, null);
        await ShowAsync(dialog);
        await dialog.Scan!.Completion;
        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.Current is not null;
        }, "the scan to be shown");

        await dialog.GoToPanelAsync();

        panels.Verify(p => p.SetUrlAsync(It.IsAny<Url>()), Times.Once);
    }

    [TestMethod]
    public async Task Escape_StopsAScanAndClosesTheDialog()
    {
        Fixture f = await OpenAsync(service: new NeverFinishingService(), waitForScan: false);
        DiskUsageScan scan = f.Dialog.Scan!;

        await f.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(scan.WasCancelled);
        Assert.IsFalse(f.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task AFolderThatCannotBeRead_IsShownAsPartial()
    {
        Fixture f = await OpenAsync(path: Path.Combine(_folder, "does-not-exist"));

        AssertScreenContains(Render(f.Root), "PARTIAL");
        AssertScreenContains(Render(f.Root), "(empty)");
        AssertScreenContains(Render(f.Root), "could not be read");
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task AnEmptyFolder_SaysSo()
    {
        string empty = Path.Combine(_folder, "empty-one");
        Directory.CreateDirectory(empty);
        Fixture f = await OpenAsync(path: empty);

        AssertScreenContains(Render(f.Root), "(empty)");
        await PressAsync(f, ConsoleKey.DownArrow);
        await PressAsync(f, ConsoleKey.Enter);
        await PressAsync(f, ConsoleKey.End);
        Assert.IsNull(f.Dialog.Selected);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task WithNothingLoaded_ItSaysThereIsNothingToMeasure()
    {
        DiskUsageDialog dialog = new(new DiskUsageService(), TestTheme());
        Control root = await ShowAsync(dialog);

        AssertScreenContains(Render(root), "Nothing to measure.");
        await dialog.GoToPanelAsync();
        dialog.Close();
    }

    private static MouseEvent ClickRow(Fixture f, int row) => new()
    {
        Position = new Position(f.Dialog.AbsolutePosition.X + 6, f.Dialog.AbsolutePosition.Y + 1 + 3 + row),
        LeftClicked = true,
    };

    [TestMethod]
    public async Task AClickOnARow_PutsTheCursorThere_AndASecondClickGoesIn()
    {
        Fixture f = await OpenAsync();

        Assert.IsTrue(await f.Dialog.HandleMouseEventAsync(ClickRow(f, 1)));
        Assert.AreEqual("small", f.Dialog.Selected!.Name);
        Assert.AreEqual(Path.GetFileName(_folder), f.Dialog.Current!.Name);

        await f.Dialog.HandleMouseEventAsync(ClickRow(f, 1));
        Assert.AreEqual("small", f.Dialog.Current!.Name);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task AClickOnAFile_OnlySelectsIt()
    {
        Fixture f = await OpenAsync();

        await f.Dialog.HandleMouseEventAsync(ClickRow(f, 2));
        await f.Dialog.HandleMouseEventAsync(ClickRow(f, 2));

        Assert.AreEqual("top.txt", f.Dialog.Selected!.Name);
        Assert.AreEqual(Path.GetFileName(_folder), f.Dialog.Current!.Name);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task AClickOutsideTheRows_DoesNothing()
    {
        Fixture f = await OpenAsync();

        await f.Dialog.HandleMouseEventAsync(ClickRow(f, 9));
        await f.Dialog.HandleMouseEventAsync(ClickRow(f, -2));
        await f.Dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });

        Assert.AreEqual("big", f.Dialog.Selected!.Name);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task TheWheel_MovesTheCursorThreeRows_AndStopsAtTheEnds()
    {
        Fixture f = await OpenAsync();
        Position inside = new(f.Dialog.AbsolutePosition.X + 6, f.Dialog.AbsolutePosition.Y + 4);

        await f.Dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = -1 });
        Assert.AreEqual("top.txt", f.Dialog.Selected!.Name);
        await f.Dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = 1 });
        Assert.AreEqual("big", f.Dialog.Selected!.Name);
        f.Dialog.Close();
    }

    [TestMethod]
    public async Task WhileScanning_TheMouseDoesNothing()
    {
        Fixture f = await OpenAsync(service: new NeverFinishingService(), waitForScan: false);

        await f.Dialog.HandleMouseEventAsync(ClickRow(f, 0));
        await f.Dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(f.Dialog.AbsolutePosition.X + 6, f.Dialog.AbsolutePosition.Y + 4), WheelDelta = -1 });

        Assert.IsNull(f.Dialog.Current);
        f.Dialog.Close();
    }
}
