using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.CommandPalette;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Shift+F1: find a command by name — narrowing, choosing with keys and mouse, running, and what it remembers.</summary>
[TestClass]
public class CommandPaletteDialogTest
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

    private sealed class Rig
    {
        public required CommandPaletteDialog Dialog { get; init; }

        public required Control Root { get; init; }

        public required MemorySettings Settings { get; init; }

        public required Dictionary<string, Mock<ICommand>> Commands { get; init; }
    }

    private static readonly (string Title, string Key, string Description)[] Catalog =
    [
        ("Calculator", "Ctrl+A", "A pocket calculator"),
        ("Copy files", "F5", "Copy the marked files to the other panel"),
        ("Delete files", "F8", "Delete the marked files"),
        ("Disk usage", "Ctrl+Shift+F12", string.Empty),
        ("Make folder", "F7", string.Empty),
        ("Properties", string.Empty, "Size, dates and attributes"),
        ("Quit", "F10", "Leave iTerminal"),
    ];

    private static async Task<Rig> OpenAsync(MemorySettings? settings = null, int? howMany = null, Size? screen = null)
    {
        settings ??= new MemorySettings();
        Dictionary<string, Mock<ICommand>> commands = [];
        List<PaletteEntry> entries = [];
        foreach ((string title, string key, string description) in Catalog.Take(howMany ?? Catalog.Length))
        {
            Mock<ICommand> command = new();
            command.Setup(c => c.RunAsync(null)).ReturnsAsync(true);
            commands[title] = command;
            entries.Add(new PaletteEntry(command.Object, title, key, description));
        }

        CommandPaletteDialog dialog = new(settings, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load(entries);
        Control root = await ShowAsync(dialog, screen);
        return new Rig { Dialog = dialog, Root = root, Settings = settings, Commands = commands };
    }

    private static Task PressAsync(Rig rig, ConsoleKey key, char c = '\0') => rig.Dialog.HandleKeyEventAsync(Key(key, c));

    private static async Task TypeAsync(Rig rig, string text)
    {
        foreach (char c in text)
        {
            await rig.Dialog.HandleKeyEventAsync(Char(c));
        }
    }

    private static string[] Titles(Rig rig) => [.. rig.Dialog.Matches.Select(m => m.Title)];

    [Timeout(60_000)]
    [TestMethod]
    public async Task ItListsEveryCommandAlphabetically_WithTheirKeys_AndTheFirstChosen()
    {
        Rig rig = await OpenAsync();

        CollectionAssert.AreEqual(Catalog.Select(c => c.Title).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray(), Titles(rig));
        Assert.AreEqual("Calculator", rig.Dialog.Selected!.Title);
        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Commands");
        AssertScreenContains(screen, "Ctrl+A");
        AssertScreenContains(screen, "F10");
        AssertScreenContains(screen, "7 of 7");
        AssertScreenContains(screen, "A pocket calculator");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingNarrowsTheList_AndTheBestMatchComesFirst()
    {
        Rig rig = await OpenAsync();

        await TypeAsync(rig, "files");
        CollectionAssert.AreEqual(new[] { "Copy files", "Delete files" }, Titles(rig));
        AssertScreenContains(Render(rig.Root), "2 of 7");

        rig.Dialog.Query = "d";
        CollectionAssert.AreEqual(new[] { "Delete files", "Disk usage" }, Titles(rig).Take(2).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AQueryCanMatchTheKeyOrTheDescription()
    {
        Rig rig = await OpenAsync();

        rig.Dialog.Query = "F10";
        CollectionAssert.AreEqual(new[] { "Quit" }, Titles(rig));
        rig.Dialog.Query = "pocket";
        CollectionAssert.AreEqual(new[] { "Calculator" }, Titles(rig));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhenNothingMatches_ItSaysSo_AndEnterRunsNothing()
    {
        Rig rig = await OpenAsync();

        await TypeAsync(rig, "zzz");

        AssertScreenContains(Render(rig.Root), "No command matches.");
        Assert.IsNull(rig.Dialog.Selected);
        await PressAsync(rig, ConsoleKey.Enter);
        Assert.IsTrue(rig.Dialog.IsVisible);
        Assert.IsFalse(await rig.Dialog.RunSelectedAsync());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheArrowsAndPages_MoveTheChoiceAndStopAtTheEnds()
    {
        Rig rig = await OpenAsync();

        await PressAsync(rig, ConsoleKey.UpArrow);
        Assert.AreEqual("Calculator", rig.Dialog.Selected!.Title);
        await PressAsync(rig, ConsoleKey.DownArrow);
        Assert.AreEqual("Copy files", rig.Dialog.Selected!.Title);
        await PressAsync(rig, ConsoleKey.PageDown);
        Assert.AreEqual("Quit", rig.Dialog.Selected!.Title);
        await PressAsync(rig, ConsoleKey.DownArrow);
        Assert.AreEqual("Quit", rig.Dialog.Selected!.Title);
        await PressAsync(rig, ConsoleKey.PageUp);
        Assert.AreEqual("Calculator", rig.Dialog.Selected!.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheChosenCommandsDescription_ShowsAboveTheEdge()
    {
        Rig rig = await OpenAsync();

        await PressAsync(rig, ConsoleKey.DownArrow);

        AssertScreenContains(Render(rig.Root), "Copy the marked files to the other panel");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_ClosesThePaletteFirst_ThenRunsTheCommand_AndRemembersIt()
    {
        Rig rig = await OpenAsync();
        await TypeAsync(rig, "quit");
        bool wasVisibleWhenRun = true;
        rig.Commands["Quit"].Setup(c => c.RunAsync(null)).Callback(() => wasVisibleWhenRun = rig.Dialog.IsVisible).ReturnsAsync(true);

        await PressAsync(rig, ConsoleKey.Enter);

        Assert.IsFalse(wasVisibleWhenRun, "the palette gives the focus back before the command runs");
        rig.Commands["Quit"].Verify(c => c.RunAsync(null), Times.Once);
        Assert.AreEqual("Quit", rig.Settings.Get(CommandPaletteDialog.RecentKey));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheCommandsRunLast_ComeFirstNextTime_NewestFirst_AndNoMoreThanEight()
    {
        MemorySettings settings = new();
        foreach (string title in new[] { "Delete files", "Properties", "Calculator" })
        {
            Rig run = await OpenAsync(settings);
            run.Dialog.Query = title;
            await PressAsync(run, ConsoleKey.Enter);
        }

        Rig next = await OpenAsync(settings);
        CollectionAssert.AreEqual(new[] { "Calculator", "Properties", "Delete files" }, Titles(next).Take(3).ToArray());
        Assert.AreEqual("Calculator\nProperties\nDelete files", settings.Get(CommandPaletteDialog.RecentKey));

        Rig again = await OpenAsync(settings);
        again.Dialog.Query = "properties";
        await PressAsync(again, ConsoleKey.Enter);
        Assert.AreEqual("Properties\nCalculator\nDelete files", settings.Get(CommandPaletteDialog.RecentKey));

        settings.Set(CommandPaletteDialog.RecentKey, string.Join('\n', Enumerable.Range(0, 8).Select(i => "old " + i)));
        Rig many = await OpenAsync(settings);
        many.Dialog.Query = "quit";
        await PressAsync(many, ConsoleKey.Enter);
        Assert.AreEqual(8, settings.Get(CommandPaletteDialog.RecentKey)!.Split('\n').Length);
        Assert.IsTrue(settings.Get(CommandPaletteDialog.RecentKey)!.StartsWith("Quit\n"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_ClosesWithoutRunningAnything()
    {
        Rig rig = await OpenAsync();

        await PressAsync(rig, ConsoleKey.Escape);

        Assert.IsFalse(rig.Dialog.IsVisible);
        foreach (Mock<ICommand> command in rig.Commands.Values)
        {
            command.Verify(c => c.RunAsync(null), Times.Never);
        }

        Assert.IsNull(rig.Settings.Get(CommandPaletteDialog.RecentKey));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOnARow_RunsThatCommand()
    {
        Rig rig = await OpenAsync();
        // The list starts three rows into the dialog and each command takes one row.
        long x = rig.Dialog.AbsolutePosition.X + 5;
        long y = rig.Dialog.AbsolutePosition.Y + 3 + 2;

        Assert.IsTrue(await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, y), LeftClicked = true }));

        rig.Commands["Delete files"].Verify(c => c.RunAsync(null), Times.Once);
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickBelowTheLastRow_OrOnTheQueryBox_RunsNothing()
    {
        Rig rig = await OpenAsync(howMany: 3);
        long x = rig.Dialog.AbsolutePosition.X + 5;

        await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, rig.Dialog.AbsolutePosition.Y + 3 + 6), LeftClicked = true });
        await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, rig.Dialog.AbsolutePosition.Y + 1), LeftClicked = true });

        Assert.IsTrue(rig.Dialog.IsVisible);
        foreach (Mock<ICommand> command in rig.Commands.Values)
        {
            command.Verify(c => c.RunAsync(null), Times.Never);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheWheel_MovesTheChoiceThreeRowsAtATime()
    {
        Rig rig = await OpenAsync();
        Position inside = new(rig.Dialog.AbsolutePosition.X + 5, rig.Dialog.AbsolutePosition.Y + 5);

        Assert.IsTrue(await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = -1 }));
        Assert.AreEqual("Disk usage", rig.Dialog.Selected!.Title);
        await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = inside, WheelDelta = 1 });
        Assert.AreEqual("Calculator", rig.Dialog.Selected!.Title);

        Assert.IsFalse(await rig.Dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = -1 }));
        Assert.AreEqual("Calculator", rig.Dialog.Selected!.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnASmallTerminal_TheChoiceStaysInTheVisibleRows()
    {
        Rig rig = await OpenAsync(screen: new Size(60, 16));

        for (int i = 0; i < 6; i++)
        {
            await PressAsync(rig, ConsoleKey.DownArrow);
        }

        Assert.AreEqual("Quit", rig.Dialog.Selected!.Title);
        AssertScreenContains(Render(rig.Root, new Size(60, 16)), "Quit");
    }
}
