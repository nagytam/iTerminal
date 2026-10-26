using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using iTerminal.Core.Services.FileAssociations;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Shift+F11 (sort order) and Open with…: choosing with keys and mouse, what is saved, and the empty list.</summary>
[TestClass]
public class SortAndOpenWithDialogTest
{
    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = [];

        public bool IsDirty => false;

        public string? Get(string key) => _values.GetValueOrDefault(key);

        public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;

        public void Set(string key, string value) => _values[key] = value;

        public void Write()
        {
        }
    }

    // ---- SortDialog ----

    private sealed class SortRig
    {
        public required SortDialog Dialog { get; init; }

        public required Control Root { get; set; }

        public required MemorySettings Settings { get; init; }

        public required Mock<ISortableContentModel> Sortable { get; init; }

        public PanelSort? Applied { get; set; }
    }

    private static async Task<SortRig> OpenSortAsync(PanelSort? current = null, bool canSort = true)
    {
        MemorySettings settings = new();
        Mock<IContentModel> model = new();
        model.SetupGet(m => m.Name).Returns("Left");
        Mock<ISortableContentModel> sortable = model.As<ISortableContentModel>();
        sortable.SetupGet(m => m.CanSort).Returns(canSort);
        sortable.SetupGet(m => m.Sort).Returns(current ?? PanelSort.AsListed);
        SortRig rig = new()
        {
            Dialog = new SortDialog(settings, TestTheme()),
            Root = null!,
            Settings = settings,
            Sortable = sortable,
        };
        sortable.Setup(m => m.SetSort(It.IsAny<PanelSort>())).Callback<PanelSort>(sort => rig.Applied = sort);
        rig.Dialog.Load(model.Object);
        rig.Root = await ShowAsync(rig.Dialog);
        return rig;
    }

    private static Task PressAsync(SortRig rig, ConsoleKey key, char c = '\0') => rig.Dialog.HandleKeyEventAsync(Key(key, c));

    [TestMethod]
    public async Task Sort_ListsTheOrders_WithTheCursorOnTheCurrentOne_AndADotOnTheAppliedOne()
    {
        SortRig rig = await OpenSortAsync(new PanelSort(PanelSortMode.Size, false));

        Assert.AreEqual(PanelSortMode.Size, rig.Dialog.Selected);
        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Sort by");
        AssertScreenContains(screen, "▶ Size");
        AssertScreenContains(screen, "Reversed");
        AssertScreenContains(screen, "Enter:apply");
    }

    [TestMethod]
    public async Task Sort_TheArrowsHomeAndEnd_MoveTheCursorWithinTheList()
    {
        SortRig rig = await OpenSortAsync();

        await PressAsync(rig, ConsoleKey.Home);
        Assert.AreEqual(SortDialog.Modes[0], rig.Dialog.Selected);
        await PressAsync(rig, ConsoleKey.UpArrow);
        Assert.AreEqual(SortDialog.Modes[0], rig.Dialog.Selected);
        await PressAsync(rig, ConsoleKey.DownArrow);
        Assert.AreEqual(SortDialog.Modes[1], rig.Dialog.Selected);
        await PressAsync(rig, ConsoleKey.End);
        Assert.AreEqual(SortDialog.Modes[^1], rig.Dialog.Selected);
        await PressAsync(rig, ConsoleKey.DownArrow);
        Assert.AreEqual(SortDialog.Modes[^1], rig.Dialog.Selected);
    }

    [TestMethod]
    public async Task Sort_EnterAppliesTheChosenOrder_SavesItForThePanel_AndCloses()
    {
        SortRig rig = await OpenSortAsync();
        await PressAsync(rig, ConsoleKey.Home);
        await PressAsync(rig, ConsoleKey.DownArrow);
        await PressAsync(rig, ConsoleKey.Spacebar, ' ');
        Assert.IsTrue(rig.Dialog.Descending);

        await PressAsync(rig, ConsoleKey.Enter);

        Assert.AreEqual(new PanelSort(SortDialog.Modes[1], true), rig.Applied);
        Assert.IsNotNull(rig.Settings.Get(PanelSort.SettingsKey("Left")));
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task Sort_AModesFirstLetterChoosesItAtOnce_AndRLeavesTheDialogOpen()
    {
        SortRig rig = await OpenSortAsync();

        await PressAsync(rig, ConsoleKey.R, 'r');
        Assert.IsTrue(rig.Dialog.Descending);
        Assert.IsTrue(rig.Dialog.IsVisible);

        await PressAsync(rig, ConsoleKey.S, 's');
        Assert.AreEqual(new PanelSort(PanelSortMode.Size, true), rig.Applied);
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task Sort_AsListedIsNeverReversed()
    {
        SortRig rig = await OpenSortAsync();
        await PressAsync(rig, ConsoleKey.R, 'r');

        await PressAsync(rig, ConsoleKey.A, 'a');

        Assert.AreEqual(new PanelSort(PanelSortMode.AsListed, false), rig.Applied);
    }

    [TestMethod]
    public async Task Sort_OnAPanelWhoseOrderIsItsContent_ChangesNothingButStillCloses()
    {
        SortRig rig = await OpenSortAsync(canSort: false);

        await PressAsync(rig, ConsoleKey.N, 'n');

        Assert.IsNull(rig.Applied);
        Assert.IsNull(rig.Settings.Get(PanelSort.SettingsKey("Left")));
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task Sort_EscapeAndKeysWithControl_DoNotApplyAnything()
    {
        SortRig rig = await OpenSortAsync();

        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('n', ConsoleKey.N, false, false, true));
        await PressAsync(rig, ConsoleKey.Escape);

        Assert.IsNull(rig.Applied);
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    private static MouseEvent SortClickRow(SortRig rig, int row) => new()
    {
        Position = new Position(rig.Dialog.AbsolutePosition.X + 6, rig.Dialog.AbsolutePosition.Y + 1 + row),
        LeftClicked = true,
    };

    [TestMethod]
    public async Task Sort_AClickOnAnOrder_AppliesIt()
    {
        SortRig rig = await OpenSortAsync();

        Assert.IsTrue(await rig.Dialog.HandleMouseEventAsync(SortClickRow(rig, 2)));

        Assert.AreEqual(new PanelSort(SortDialog.Modes[2], false), rig.Applied);
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task Sort_AClickOnReversed_FlipsItAndLeavesTheDialogOpen()
    {
        SortRig rig = await OpenSortAsync();

        await rig.Dialog.HandleMouseEventAsync(SortClickRow(rig, SortDialog.Modes.Count + 1));

        Assert.IsTrue(rig.Dialog.Descending);
        Assert.IsTrue(rig.Dialog.IsVisible);
        AssertScreenContains(Render(rig.Root), "[x] Reversed");
        await rig.Dialog.HandleMouseEventAsync(SortClickRow(rig, SortDialog.Modes.Count + 1));
        Assert.IsFalse(rig.Dialog.Descending);
    }

    [TestMethod]
    public async Task Sort_AClickOnTheBlankLineOrOutside_DoesNothing_AndTheWheelMovesTheCursor()
    {
        SortRig rig = await OpenSortAsync(new PanelSort(PanelSortMode.Name, false));

        await rig.Dialog.HandleMouseEventAsync(SortClickRow(rig, SortDialog.Modes.Count));
        await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });
        Assert.IsNull(rig.Applied);
        Assert.IsTrue(rig.Dialog.IsVisible);

        Position inside = new(rig.Dialog.AbsolutePosition.X + 6, rig.Dialog.AbsolutePosition.Y + 2);
        await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = -1 });
        Assert.AreEqual(PanelSortMode.Extension, rig.Dialog.Selected);
        await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = 1 });
        await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = 1 });
        Assert.AreEqual(PanelSortMode.Name, rig.Dialog.Selected);
    }

    // ---- OpenWithDialog ----

    private sealed class OpenRig
    {
        public required OpenWithDialog Dialog { get; init; }

        public required Control Root { get; init; }

        public required FileAssociationService Associations { get; init; }

        public required Mock<IFileLauncher> Launcher { get; init; }

        public required FileAssociationRunner Runner { get; init; }
    }

    private static FileAssociation Program(string id, string name, string patterns = "*.txt", FileAssociationKind kind = FileAssociationKind.Open, bool inTerminal = false)
        => new(id, name, patterns, name.ToLowerInvariant() + " %f", kind, inTerminal);

    private static async Task<OpenRig> OpenWithAsync(
        FileAssociationKind kind = FileAssociationKind.Open,
        FileAssociation[]? programs = null,
        Func<Task>? builtInEditor = null,
        Size? screen = null)
    {
        FileAssociationService associations = new(new MemorySettings());
        foreach (FileAssociation program in (programs ?? []).Reverse())
        {
            associations.Save(program);
        }

        Mock<IFileLauncher> launcher = new();
        launcher.Setup(l => l.LaunchAsync(It.IsAny<FileAssociation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(LaunchOutcome.Success);
        launcher.Setup(l => l.OpenWithSystemDefaultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(LaunchOutcome.Success);
        Mock<IContentModelService> panels = new();
        panels.SetupGet(p => p.ContentModels).Returns([]);
        FileAssociationRunner runner = new(associations, launcher.Object, panels.Object);
        FileAssociationDialog form = new(associations, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        OpenWithDialog dialog = new(runner, form, TestTheme());
        dialog.Load(kind, Path.Combine(Path.GetTempPath(), "notes.txt"), builtInEditor);
        Control root = await ShowAsync(dialog, screen);
        return new OpenRig { Dialog = dialog, Root = root, Associations = associations, Launcher = launcher, Runner = runner };
    }

    private static Task PressAsync(OpenRig rig, ConsoleKey key, char c = '\0') => rig.Dialog.HandleKeyEventAsync(Key(key, c));

    private static readonly FileAssociation[] TwoPrograms = [Program("a", "Notepad"), Program("b", "Editor")];

    [TestMethod]
    public async Task OpenWith_ListsTheProgramsDefaultFirst_ThenTheSystemDefault()
    {
        OpenRig rig = await OpenWithAsync(programs: TwoPrograms);

        CollectionAssert.AreEqual(new[] { "Notepad", "Editor", "System default" }, rig.Dialog.Choices.Select(c => c.Title).ToArray());
        Assert.IsTrue(rig.Dialog.Choices[0].IsDefault);
        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Open notes.txt with");
        AssertScreenContains(screen, "Notepad");
        AssertScreenContains(screen, "default");
        AssertScreenContains(screen, "notepad %f");
    }

    [TestMethod]
    public async Task OpenWith_ForEditing_OffersTheBuiltInEditorOnlyWhenThereIsOne()
    {
        OpenRig withEditor = await OpenWithAsync(FileAssociationKind.Edit, builtInEditor: () => Task.CompletedTask);
        CollectionAssert.AreEqual(new[] { "iTerminal editor" }, withEditor.Dialog.Choices.Select(c => c.Title).ToArray());
        AssertScreenContains(Render(withEditor.Root), "Edit notes.txt with");

        OpenRig without = await OpenWithAsync(FileAssociationKind.Edit);
        Assert.AreEqual(0, without.Dialog.Choices.Count);
    }

    [TestMethod]
    public async Task OpenWith_WithNoProgramAtAll_SaysHowToAddOne_AndEnterDoesNothing()
    {
        OpenRig rig = await OpenWithAsync(FileAssociationKind.Edit);

        AssertScreenContains(Render(rig.Root), "No program yet - Ins adds one.");
        Assert.IsNull(rig.Dialog.Selected);
        await PressAsync(rig, ConsoleKey.Enter);
        Assert.IsTrue(rig.Dialog.IsVisible);
        Assert.IsFalse(await rig.Dialog.OpenSelectedAsync());
    }

    [TestMethod]
    public async Task OpenWith_EnterOpensTheFileWithTheChosenProgram_AndCloses()
    {
        OpenRig rig = await OpenWithAsync(programs: TwoPrograms);
        await PressAsync(rig, ConsoleKey.DownArrow);

        await PressAsync(rig, ConsoleKey.Enter);
        await rig.Runner.LastRun;

        rig.Launcher.Verify(l => l.LaunchAsync(TwoPrograms[1], rig.Dialog.FilePath, It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task OpenWith_TheSystemDefaultRow_OpensItWithTheSystem()
    {
        OpenRig rig = await OpenWithAsync(programs: TwoPrograms);
        await PressAsync(rig, ConsoleKey.End);

        await PressAsync(rig, ConsoleKey.Enter);
        await rig.Runner.LastRun;

        rig.Launcher.Verify(l => l.OpenWithSystemDefaultAsync(rig.Dialog.FilePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task OpenWith_TheBuiltInEditorRow_RunsTheEditorCallback()
    {
        bool opened = false;
        OpenRig rig = await OpenWithAsync(FileAssociationKind.Edit, builtInEditor: () =>
        {
            opened = true;
            return Task.CompletedTask;
        });

        await PressAsync(rig, ConsoleKey.Enter);

        Assert.IsTrue(opened);
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task OpenWith_PagesHomeAndEnd_MoveWithinTheList()
    {
        FileAssociation[] many = Enumerable.Range(0, 20).Select(i => Program("id" + i, "Program " + i.ToString("00"))).ToArray();
        OpenRig rig = await OpenWithAsync(programs: many, screen: new Size(60, 14));

        await PressAsync(rig, ConsoleKey.PageDown);
        int afterPage = rig.Dialog.Choices.ToList().IndexOf(rig.Dialog.Selected!);
        Assert.IsTrue(afterPage > 1 && afterPage < 20, afterPage.ToString());
        await PressAsync(rig, ConsoleKey.End);
        Assert.AreEqual("System default", rig.Dialog.Selected!.Title);
        await PressAsync(rig, ConsoleKey.PageUp);
        Assert.IsTrue(rig.Dialog.Choices.ToList().IndexOf(rig.Dialog.Selected!) < 20);
        await PressAsync(rig, ConsoleKey.Home);
        Assert.AreEqual("Program 00", rig.Dialog.Selected!.Title);
    }

    [TestMethod]
    public async Task OpenWith_DelAsksFirst_OnlyYRemoves_AndTheRowsThatAreAlwaysThereCannotBeRemoved()
    {
        OpenRig rig = await OpenWithAsync(programs: TwoPrograms);

        await PressAsync(rig, ConsoleKey.Delete);
        AssertScreenContains(Render(rig.Root), "Remove Notepad from the list? (y/n)");
        await PressAsync(rig, ConsoleKey.N, 'n');
        Assert.AreEqual(2, rig.Associations.GetAll().Count);
        Assert.AreEqual(string.Empty, rig.Dialog.Question);

        await PressAsync(rig, ConsoleKey.Delete);
        await PressAsync(rig, ConsoleKey.Y, 'y');
        Assert.AreEqual(1, rig.Associations.GetAll().Count);
        CollectionAssert.AreEqual(new[] { "Editor", "System default" }, rig.Dialog.Choices.Select(c => c.Title).ToArray());

        await PressAsync(rig, ConsoleKey.End);
        await PressAsync(rig, ConsoleKey.Delete);
        Assert.AreEqual(string.Empty, rig.Dialog.Question);
    }

    [TestMethod]
    public async Task OpenWith_SpaceMakesTheChosenProgramTheDefault()
    {
        OpenRig rig = await OpenWithAsync(programs: TwoPrograms);
        await PressAsync(rig, ConsoleKey.DownArrow);

        await PressAsync(rig, ConsoleKey.Spacebar, ' ');

        Assert.AreEqual("Editor", rig.Dialog.Choices[0].Title);
        Assert.IsTrue(rig.Dialog.Choices[0].IsDefault);
        Assert.AreEqual("Editor", rig.Dialog.Selected!.Title);
    }

    [TestMethod]
    public async Task OpenWith_ListChangedUnderneath_IsFollowedOnIdle()
    {
        OpenRig rig = await OpenWithAsync(programs: TwoPrograms);

        rig.Associations.Save(Program("c", "Brand new"));
        rig.Dialog.OnIdle();

        Assert.IsTrue(rig.Dialog.Choices.Any(c => c.Title == "Brand new"));
        Assert.AreEqual(4, rig.Dialog.Choices.Count);
    }

    private static MouseEvent OpenWithClickRow(OpenRig rig, int row) => new()
    {
        Position = new Position(rig.Dialog.AbsolutePosition.X + 6, rig.Dialog.AbsolutePosition.Y + 1 + row),
        LeftClicked = true,
    };

    [TestMethod]
    public async Task OpenWith_AClickOnARow_OpensTheFileWithIt()
    {
        OpenRig rig = await OpenWithAsync(programs: TwoPrograms);

        Assert.IsTrue(await rig.Dialog.HandleMouseEventAsync(OpenWithClickRow(rig, 1)));
        await rig.Runner.LastRun;

        rig.Launcher.Verify(l => l.LaunchAsync(TwoPrograms[1], rig.Dialog.FilePath, It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task OpenWith_AClickBelowTheRows_OrWhileTheQuestionIsUp_DoesNothing()
    {
        OpenRig rig = await OpenWithAsync(programs: TwoPrograms);

        await rig.Dialog.HandleMouseEventAsync(OpenWithClickRow(rig, 5));
        Assert.IsTrue(rig.Dialog.IsVisible);

        await PressAsync(rig, ConsoleKey.Delete);
        await rig.Dialog.HandleMouseEventAsync(OpenWithClickRow(rig, 0));
        Assert.IsTrue(rig.Dialog.IsVisible);
        Assert.AreNotEqual(string.Empty, rig.Dialog.Question);
        rig.Launcher.Verify(l => l.LaunchAsync(It.IsAny<FileAssociation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task OpenWith_TheWheel_MovesTheChoiceThreeRows()
    {
        FileAssociation[] many = Enumerable.Range(0, 10).Select(i => Program("id" + i, "Program " + i.ToString("00"))).ToArray();
        OpenRig rig = await OpenWithAsync(programs: many);
        Position inside = new(rig.Dialog.AbsolutePosition.X + 6, rig.Dialog.AbsolutePosition.Y + 2);

        await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = -1 });
        Assert.AreEqual("Program 03", rig.Dialog.Selected!.Title);
        await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = 1 });
        Assert.AreEqual("Program 00", rig.Dialog.Selected!.Title);
    }

    [TestMethod]
    public async Task Sort_KeyHints_NameTheKeysTheDialogTakes()
    {
        SortRig rig = await OpenSortAsync();

        string[] keys = [.. rig.Dialog.GetKeyHints().Select(h => h.Key)];

        CollectionAssert.IsSubsetOf(new[] { "Up/Down", "Enter", "R/Space" }, keys);
    }

    [TestMethod]
    public async Task OpenWith_KeyHints_ListTheCommands_AndWhileTheRemoveQuestionIsUpOnlyItsAnswers()
    {
        OpenRig rig = await OpenWithAsync(programs: TwoPrograms);

        string[] keys = [.. rig.Dialog.GetKeyHints().Select(h => h.Key)];
        CollectionAssert.IsSubsetOf(new[] { "Enter", "Insert", "F4", "Delete", "Space" }, keys);

        await PressAsync(rig, ConsoleKey.Delete);

        keys = [.. rig.Dialog.GetKeyHints().Select(h => h.Key)];
        CollectionAssert.AreEqual(new[] { "Y", "Any other key" }, keys);
    }
}
