using iTerminal.Core.Dialogs.Workspaces;
using iTerminal.Core.Test.Model.Map;
using iTerminal.Core.Model.Workspaces;
using iTerminal.Core.Services.Workspaces;
using iTerminal.Core.Test.Services.Workspaces;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Workspaces;

// Some tests put text on the ambient clipboard and paste it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class WorkspaceManagerDialogTest
{
    private static (WorkspaceManagerDialog Dialog, WorkspaceService Service, FakeLayout Layout) Create(params string[] saved)
    {
        var layout = new FakeLayout();
        var service = new WorkspaceService(new MemorySettings(), layout);
        foreach (var name in saved)
        {
            layout.Current = layout.Current with { LeftUrl = $@"file://C:\work\{name.ToLowerInvariant()}" };
            service.Save(name);
        }

        return (new WorkspaceManagerDialog(service, TestTheme()), service, layout);
    }

    private static async Task TypeAsync(WorkspaceManagerDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    [TestMethod]
    public async Task AnEmptyListExplainsWhatSavingDoes()
    {
        var (dialog, _, _) = Create();

        await ShowAsync(dialog);

        var screen = Render(dialog);
        AssertScreenContains(screen, "0 saved");
        AssertScreenContains(screen, "No workspaces yet. S saves the screen as it is now");
        AssertScreenContains(screen, "Enter switch  1-9 jump  S save");
    }

    [TestMethod]
    public async Task TheListShowsEachWorkspaceWithItsNumberAndFolders()
    {
        var (dialog, _, _) = Create("Alpha", "Beta");

        await ShowAsync(dialog);

        var screen = Render(dialog);
        AssertScreenContains(screen, "2 saved");
        AssertScreenContains(screen, "1 Alpha");
        AssertScreenContains(screen, "2 Beta");
        AssertScreenContains(screen, "alpha");
        AssertScreenContains(screen, "→ docs");
    }

    [TestMethod]
    public async Task TheLastSavedIsMarkedAsWhatIsOnScreen()
    {
        var (dialog, _, _) = Create("Alpha");

        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "● This is what is on screen now.");
    }

    [TestMethod]
    public async Task ARowSaysWhenTheScreenHasMovedOnSinceItWasSaved()
    {
        var (dialog, _, layout) = Create("Alpha");
        layout.Current = layout.Current with { RightUrl = @"file://C:\somewhere\else" };

        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "○ The screen has changed since. U updates this");
    }

    [TestMethod]
    public async Task SSavesTheScreenUnderTheSuggestedNameOnEnter()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('s'));
        AssertScreenContains(Render(dialog), "Save the screen as a workspace:");
        AssertScreenContains(Render(dialog), "Workspace 1");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual("Workspace 1", service.GetAll().Single().Name);
        AssertScreenContains(Render(dialog), "Saved 'Workspace 1'.");
    }

    [TestMethod]
    public async Task ANameCanBeTypedInPlaceOfTheSuggestion()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('s'));

        await TypeAsync(dialog, "Build box");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual("Build box", service.GetAll().Single().Name);
    }

    [TestMethod]
    public async Task AnEmptyNameIsRefusedAndTheFormStaysOpen()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('s'));
        // The suggestion is selected, so one Backspace clears the whole name.
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        AssertScreenContains(Render(dialog), "Give the workspace a name.");
        Assert.AreEqual(0, service.GetAll().Count);
    }

    [TestMethod]
    public async Task ATakenNameNeedsASecondEnterToReplaceIt()
    {
        var (dialog, service, layout) = Create("Alpha");
        layout.Current = layout.Current with { LeftUrl = @"file://C:\new\place" };
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('s'));

        await TypeAsync(dialog, "alpha");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        AssertScreenContains(Render(dialog), "already exists. Enter again replaces it");
        Assert.AreEqual(@"file://C:\work\alpha", service.GetAll().Single().Layout.LeftUrl);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(@"file://C:\new\place", service.GetAll().Single().Layout.LeftUrl);
        AssertScreenContains(Render(dialog), "Replaced 'Alpha'.");
    }

    [TestMethod]
    public async Task EscInTheNameFormGoesBackToTheListWithoutSaving()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('s'));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.AreEqual(0, service.GetAll().Count);
        AssertScreenContains(Render(dialog), "No workspaces yet.");
    }

    [TestMethod]
    public async Task EnterSwitchesToTheSelectedWorkspaceAndClosesTheDialog()
    {
        var (dialog, _, layout) = Create("Alpha", "Beta");
        var root = await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(@"file://C:\work\alpha", layout.Applied.Single().LeftUrl);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public async Task ANumberKeyJumpsStraightToThatWorkspace()
    {
        var (dialog, service, layout) = Create("Alpha", "Beta", "Gamma");
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('2'));

        Assert.AreEqual(@"file://C:\work\beta", layout.Applied.Single().LeftUrl);
        Assert.AreEqual("Beta", service.ActiveName);
    }

    [TestMethod]
    public async Task ANumberWithNoWorkspaceBehindItSaysSoAndKeepsTheDialog()
    {
        var (dialog, _, layout) = Create("Alpha");
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('7'));

        AssertScreenContains(Render(dialog), "There is no workspace 7.");
        Assert.AreEqual(0, layout.Applied.Count);
    }

    [TestMethod]
    public async Task DeleteAsksFirstAndOnlyYDeletes()
    {
        var (dialog, service, _) = Create("Alpha", "Beta");
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('d'));
        AssertScreenContains(Render(dialog), "Delete 'Beta'? y deletes, any other key keeps it.");
        await dialog.HandleKeyEventAsync(Char('x'));
        Assert.AreEqual(2, service.GetAll().Count);

        await dialog.HandleKeyEventAsync(Char('d'));
        await dialog.HandleKeyEventAsync(Char('y'));

        CollectionAssert.AreEqual(new[] { "Alpha" }, service.GetAll().Select(w => w.Name).ToArray());
        AssertScreenContains(Render(dialog), "Deleted 'Beta'.");
    }

    [TestMethod]
    public async Task UpdateReplacesTheSelectedWithTheScreenAfterAYes()
    {
        var (dialog, service, layout) = Create("Alpha");
        layout.Current = layout.Current with { Bar = WorkspaceBar.Terminal };
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('u'));
        AssertScreenContains(Render(dialog), "Replace 'Alpha' with the screen as it is now?");
        Assert.AreEqual(WorkspaceBar.Ai, service.GetAll().Single().Layout.Bar);
        await dialog.HandleKeyEventAsync(Char('y'));

        Assert.AreEqual(WorkspaceBar.Terminal, service.GetAll().Single().Layout.Bar);
    }

    [TestMethod]
    public async Task RenameStartsFromTheOldNameAndEndKeepsItToEdit()
    {
        var (dialog, service, _) = Create("Alpha");
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('r'));
        AssertScreenContains(Render(dialog), "Rename 'Alpha':");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.End));
        await TypeAsync(dialog, "2");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual("Alpha2", service.GetAll().Single().Name);
    }

    [TestMethod]
    public async Task TypingOverTheSelectedOldNameReplacesIt()
    {
        var (dialog, service, _) = Create("Alpha");
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('r'));
        await TypeAsync(dialog, "Zed");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual("Zed", service.GetAll().Single().Name);
    }

    [TestMethod]
    public async Task EscClosesTheDialogFromTheList()
    {
        var (dialog, _, _) = Create("Alpha");
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public void AShortLabelIsTheLastFolderOrTheScheme()
    {
        Assert.AreEqual("project", WorkspaceManagerDialog.Short(@"file://C:\work\project"));
        Assert.AreEqual("-", WorkspaceManagerDialog.Short(string.Empty));
    }

    [TestMethod]
    public async Task CtrlVPastesTheNameInPlaceOfTheSuggestion_OnOneLine()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('s'));
        await iConsole.Services.TextClipboard.Current.SetTextAsync("Build" + (char)10 + "box");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v', control: true));
        AssertScreenContains(Render(dialog), "Build box");
        AssertScreenDoesNotContain(Render(dialog), "Workspace 1");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual("Build box", service.GetAll().Single().Name);
    }

    [TestMethod]
    public async Task APastedNameIsCutToTheLongestAllowed()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('s'));
        await iConsole.Services.TextClipboard.Current.SetTextAsync(new string('x', 500));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v', control: true));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(iTerminal.Core.Services.Workspaces.IWorkspaceService.MaxNameLength, service.GetAll().Single().Name.Length);
    }

    // The list starts after the border, the heading and the column header: three rows down, two columns in.
    private static iConsole.Data.MouseEvent At(iConsole.Controls.Control dialog, int row, int column = 8, int wheel = 0, bool click = true) => new()
    {
        Position = new iConsole.Data.Position(dialog.AbsolutePosition.X + 2 + column, dialog.AbsolutePosition.Y + 3 + row),
        LeftClicked = click && wheel == 0,
        WheelDelta = wheel,
    };

    [TestMethod]
    public async Task AClickHighlightsAWorkspace_AndTheWheelMovesThreeAtATime()
    {
        var (dialog, _, _) = Create("A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8");
        await ShowAsync(dialog);

        Assert.IsTrue(await dialog.HandleMouseEventAsync(At(dialog, 2)));
        Assert.AreEqual(2, dialog.CursorForTest);

        await dialog.HandleMouseEventAsync(At(dialog, 0, wheel: -1));
        Assert.AreEqual(5, dialog.CursorForTest);
        await dialog.HandleMouseEventAsync(At(dialog, 0, wheel: 1));
        await dialog.HandleMouseEventAsync(At(dialog, 0, wheel: 1));
        Assert.AreEqual(0, dialog.CursorForTest);
    }

    [TestMethod]
    public async Task AClickDoesNotSwitchWorkspaces_EnterDoes_AndAClickBelowTheListDoesNothing()
    {
        var (dialog, _, layout) = Create("Alpha", "Beta");
        await ShowAsync(dialog);
        string before = layout.Current.LeftUrl;

        await dialog.HandleMouseEventAsync(At(dialog, 1));
        Assert.AreEqual(1, dialog.CursorForTest);
        Assert.AreEqual(before, layout.Current.LeftUrl, "a click only highlights");

        await dialog.HandleMouseEventAsync(At(dialog, 6));
        Assert.AreEqual(1, dialog.CursorForTest);
    }

    [TestMethod]
    public async Task TheMouseLeavesTheListAloneWhileANameIsBeingTyped()
    {
        var (dialog, _, _) = Create("Alpha", "Beta", "Gamma");
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('s'));
        int before = dialog.CursorForTest;

        await dialog.HandleMouseEventAsync(At(dialog, before == 0 ? 2 : 0));

        Assert.AreEqual(before, dialog.CursorForTest);
    }
}
