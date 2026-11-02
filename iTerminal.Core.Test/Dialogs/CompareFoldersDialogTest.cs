using iConsole.Controls;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Compare;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Shift+F2: comparing the two panels' folders, the list of differences and how to scroll it, and the two-step F5.</summary>
[TestClass]
public class CompareFoldersDialogTest
{
    private static readonly DateTime Noon = new(2024, 5, 6, 12, 0, 0);
    private static readonly Size Small = new(80, 23);

    private string _root = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "compare-dialog-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "L"));
        Directory.CreateDirectory(Path.Combine(_root, "R"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private void Write(string side, string name, string content, DateTime time)
    {
        string path = Path.Combine(_root, side, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, time);
    }

    private static IContentModel Panel(string name, string? folder)
    {
        Mock<IContentModel> model = new();
        model.SetupGet(m => m.Name).Returns(name);
        if (folder is not null)
        {
            model.SetupGet(m => m.Url).Returns(new Url(folder + Path.DirectorySeparatorChar));
        }
        return model.Object;
    }

    private async Task<(CompareFoldersDialog Dialog, Control Root)> OpenAsync(bool bothPanels = true, Size? screen = null)
    {
        Mock<IContentModelService> panels = new();
        ConcurrentList<IContentModel> list = [];
        list.Add(Panel(ContentModelService.Left, bothPanels ? Path.Combine(_root, "L") : null));
        list.Add(Panel(ContentModelService.Right, bothPanels ? Path.Combine(_root, "R") : null));
        panels.SetupGet(p => p.ContentModels).Returns(list);
        FileSystemResolver resolver = new(new FileSystemService(), null!, null!, null!, panels.Object);
        CompareFoldersDialog dialog = new(panels.Object, resolver, TestTheme());
        Control root = await ShowAsync(dialog, screen ?? Small);
        return (dialog, root);
    }

    private static Task PressAsync(CompareFoldersDialog dialog, ConsoleKey key) => dialog.HandleKeyEventAsync(Key(key));

    private static async Task CompareAsync(CompareFoldersDialog dialog)
    {
        await PressAsync(dialog, ConsoleKey.Enter);
        await dialog.CompareTask;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnOpening_ItNamesBothFolders_AndSaysEnterCompares()
    {
        (CompareFoldersDialog dialog, Control root) = await OpenAsync();

        ScreenBuffer screen = Render(root, Small);
        AssertScreenContains(screen, "Compare and synchronise folders");
        AssertScreenContains(screen, "Enter compares.");
        AssertScreenContains(screen, "Left:  ");
        AssertScreenContains(screen, "Right: ");
        AssertScreenContains(screen, "Enter:compare");
        Assert.IsNull(dialog.Result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutTwoFolders_EnterSaysSo()
    {
        (CompareFoldersDialog dialog, Control root) = await OpenAsync(bothPanels: false);

        await PressAsync(dialog, ConsoleKey.Enter);

        Assert.AreEqual("Both panels must show a folder.", dialog.Status);
        AssertScreenContains(Render(root, Small), "Both panels must show a folder.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_ComparesTheFolders_ListsOnlyTheDifferences_AndSummarises()
    {
        Write("L", "same.txt", "same", Noon);
        Write("R", "same.txt", "same", Noon);
        Write("L", "left.txt", "only here", Noon);
        Write("R", "right.txt", "only there", Noon);
        (CompareFoldersDialog dialog, Control root) = await OpenAsync();

        await CompareAsync(dialog);

        Assert.IsNotNull(dialog.Result);
        ScreenBuffer screen = Render(root, Small);
        AssertScreenContains(screen, "◀ only left");
        AssertScreenContains(screen, "left.txt");
        AssertScreenContains(screen, "only right ▶");
        AssertScreenContains(screen, "right.txt");
        AssertScreenDoesNotContain(screen, "same.txt");
        StringAssert.Contains(dialog.Status, "1 only on the left");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task IdenticalFolders_SayNothingDiffers()
    {
        Write("L", "a.txt", "a", Noon);
        Write("R", "a.txt", "a", Noon);
        (CompareFoldersDialog dialog, _) = await OpenAsync();

        await CompareAsync(dialog);

        Assert.AreEqual(0, dialog.Result!.Differences.Count());
        StringAssert.Contains(dialog.Status, "1 identical");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheModeAndSubfolderOptions_ChangeWhatIsFound()
    {
        Write("L", "a.txt", "aaaa", Noon);
        Write("R", "a.txt", "bbbb", Noon);
        Directory.CreateDirectory(Path.Combine(_root, "L", "sub"));
        File.WriteAllText(Path.Combine(_root, "L", "sub", "deep.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "R", "sub"));
        File.WriteAllText(Path.Combine(_root, "R", "sub", "other.txt"), "y");
        (CompareFoldersDialog dialog, _) = await OpenAsync();

        await CompareAsync(dialog);
        Assert.AreEqual(DiffKind.Identical, dialog.Result!.Items.Single(i => i.Path == "a.txt").Kind);
        Assert.IsFalse(dialog.Result.Items.Any(i => i.Path == "sub/deep.txt"));

        dialog.Mode = CompareMode.Content;
        dialog.IncludeSubfolders = true;
        await CompareAsync(dialog);
        Assert.AreEqual(DiffKind.Different, dialog.Result!.Items.Single(i => i.Path == "a.txt").Kind);
        Assert.IsTrue(dialog.Result.Items.Any(i => i.Path == "sub/deep.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADifferenceListLongerThanTheView_ScrollsWithTheKeys_AndTheWheel()
    {
        for (int i = 0; i < 30; i++)
        {
            Write("L", "file" + i.ToString("00") + ".txt", "x", Noon);
        }

        (CompareFoldersDialog dialog, Control root) = await OpenAsync();
        await CompareAsync(dialog);
        AssertScreenContains(Render(root, Small), "file00.txt");
        AssertScreenDoesNotContain(Render(root, Small), "file29.txt");

        // The compare-mode list has focus at first and keeps the arrows, Home and End for itself;
        // two Tabs take the focus out of it and its form.
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '	'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '	'));
        await PressAsync(dialog, ConsoleKey.End);
        AssertScreenContains(Render(root, Small), "file29.txt");
        AssertScreenDoesNotContain(Render(root, Small), "file00.txt");

        await PressAsync(dialog, ConsoleKey.Home);
        AssertScreenContains(Render(root, Small), "file00.txt");

        await PressAsync(dialog, ConsoleKey.PageDown);
        AssertScreenDoesNotContain(Render(root, Small), "file00.txt");
        await PressAsync(dialog, ConsoleKey.PageUp);
        AssertScreenContains(Render(root, Small), "file00.txt");

        Position inside = new(dialog.AbsolutePosition.X + 10, dialog.AbsolutePosition.Y + 12);
        Assert.IsTrue(await dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = -1 }));
        AssertScreenDoesNotContain(Render(root, Small), "file00.txt");
        AssertScreenContains(Render(root, Small), "file03.txt");
        await dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = 1 });
        AssertScreenContains(Render(root, Small), "file00.txt");
        await dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = 1 });
        AssertScreenContains(Render(root, Small), "file00.txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ThePageKeysScrollTheDifferences_EvenWhileTheCompareModeListHasFocus()
    {
        for (int i = 0; i < 30; i++)
        {
            Write("L", "file" + i.ToString("00") + ".txt", "x", Noon);
        }

        (CompareFoldersDialog dialog, Control root) = await OpenAsync();
        await CompareAsync(dialog);
        AssertScreenContains(Render(root, Small), "file00.txt");

        await PressAsync(dialog, ConsoleKey.PageDown);

        AssertScreenDoesNotContain(Render(root, Small), "file00.txt");
        Assert.AreEqual(CompareMode.SizeAndDate, dialog.Mode, "the page key must not change the compare mode");

        await PressAsync(dialog, ConsoleKey.PageUp);
        AssertScreenContains(Render(root, Small), "file00.txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheWheelOutsideTheDialog_IsNotTheDialogs()
    {
        (CompareFoldersDialog dialog, _) = await OpenAsync();

        Assert.IsFalse(await dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = -1 }));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F5BeforeAnyComparison_SaysToCompareFirst()
    {
        (CompareFoldersDialog dialog, _) = await OpenAsync();

        await PressAsync(dialog, ConsoleKey.F5);

        StringAssert.Contains(dialog.Status, "Compare first");
        Assert.IsNull(dialog.PendingPlan);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F5WithNothingToDo_SaysSo()
    {
        Write("L", "a.txt", "a", Noon);
        Write("R", "a.txt", "a", Noon);
        (CompareFoldersDialog dialog, _) = await OpenAsync();
        await CompareAsync(dialog);

        await PressAsync(dialog, ConsoleKey.F5);

        Assert.IsNull(dialog.PendingPlan);
        Assert.AreEqual(FolderSynchronizer.Plan(dialog.Result!, new SyncOptions(dialog.Direction, dialog.DeleteExtras)).Summary(), dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F5ProposesThePlan_AndAnyOtherKeyWithdrawsIt()
    {
        Write("L", "left.txt", "only here", Noon);
        (CompareFoldersDialog dialog, Control root) = await OpenAsync();
        await CompareAsync(dialog);

        await PressAsync(dialog, ConsoleKey.F5);

        Assert.IsNotNull(dialog.PendingPlan);
        StringAssert.Contains(dialog.Status, "F5 again to do it, any other key to cancel.");
        AssertScreenContains(Render(root, Small), "F5 again");

        await PressAsync(dialog, ConsoleKey.DownArrow);

        Assert.IsNull(dialog.PendingPlan);
        StringAssert.Contains(dialog.Status, "nothing was changed");
        Assert.IsTrue(dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASecondF5WithoutTheMachineryToRunIt_SaysSoAndKeepsTheDialog()
    {
        Write("L", "left.txt", "only here", Noon);
        (CompareFoldersDialog dialog, _) = await OpenAsync();
        await CompareAsync(dialog);
        await PressAsync(dialog, ConsoleKey.F5);

        await PressAsync(dialog, ConsoleKey.F5);

        Assert.AreEqual("Synchronising is not available here.", dialog.Status);
        Assert.IsNull(dialog.PendingPlan);
        Assert.IsTrue(dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheDirectionAndDeleteExtras_FeedThePlan()
    {
        Write("R", "extra.txt", "only there", Noon);
        (CompareFoldersDialog dialog, _) = await OpenAsync();
        await CompareAsync(dialog);

        dialog.Direction = SyncDirection.LeftToRight;
        dialog.DeleteExtras = true;
        await PressAsync(dialog, ConsoleKey.F5);

        Assert.IsNotNull(dialog.PendingPlan);
        Assert.AreEqual(1, dialog.PendingPlan!.Deletes);
        StringAssert.Contains(dialog.Status, "recycle bin");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_Closes()
    {
        (CompareFoldersDialog dialog, _) = await OpenAsync();

        await PressAsync(dialog, ConsoleKey.Escape);

        Assert.IsFalse(dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DescribeKind_NamesEveryKind()
    {
        Assert.AreEqual("◀ only left", CompareFoldersDialog.DescribeKind(DiffKind.LeftOnly));
        Assert.AreEqual("only right ▶", CompareFoldersDialog.DescribeKind(DiffKind.RightOnly));
        Assert.AreEqual("◀ newer left", CompareFoldersDialog.DescribeKind(DiffKind.LeftNewer));
        Assert.AreEqual("newer right ▶", CompareFoldersDialog.DescribeKind(DiffKind.RightNewer));
        Assert.AreEqual("≠ different", CompareFoldersDialog.DescribeKind(DiffKind.Different));
        Assert.AreEqual("= identical", CompareFoldersDialog.DescribeKind(DiffKind.Identical));
    }
}
