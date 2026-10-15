using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iTerminal.Core.Services.CommandPalette;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Services.CommandPalette;

/// <summary>What Shift+F1 lists and in which order.</summary>
[TestClass]
public class CommandCatalogTest
{
    private sealed class TestCommand(string title, Key? key, bool canExecute = true, string description = "") : Command
    {
        public override string Title => title;
        public override string Description => description.Length > 0 ? description : title;
        public override Key? Key { get; init; } = key;
        public override bool CanExecute() => canExecute;
        public override Task ExecuteAsync(object? args) => Task.CompletedTask;
    }

    private static Key K(ConsoleKey key, bool shift = false, bool alt = false, bool control = false) => new(key, shift, alt, control);

    private static Window Tree(params (Control Control, ICommand[] Commands)[] children)
    {
        var root = new Window(TestTheme());
        foreach (var (control, commands) in children)
        {
            foreach (var command in commands)
            {
                control.AddCommand(command);
            }

            root.AddControl(control);
        }

        return root;
    }

    private static string[] Titles(IEnumerable<PaletteEntry> entries) => [.. entries.Select(e => e.Title)];

    [TestMethod]
    public void HiddenControlsCountButCursorKeysAndCommandsThatCannotRunDoNot()
    {
        // The hot key bar's Alt layer is hidden, and it is where most features live.
        var hidden = new Window(TestTheme()) { IsVisible = false };
        var root = Tree(
            (new Window(TestTheme()),
            [
                new TestCommand("Calculator", K(ConsoleKey.A, control: true)),
                new TestCommand("Move down", K(ConsoleKey.DownArrow)),
                new TestCommand("Type", K(ConsoleKey.A)),
                new TestCommand("Open", K(ConsoleKey.Enter)),
                new TestCommand("Copy", K(ConsoleKey.F5), canExecute: false),
                new TestCommand("Mark", K(ConsoleKey.Insert)),
                new TestCommand("Mark down", K(ConsoleKey.DownArrow, shift: true)),
            ]),
            (hidden, [new TestCommand("Packages", K(ConsoleKey.F8, alt: true))]));

        var entries = CommandCatalog.Collect(root);

        CollectionAssert.AreEquivalent(new[] { "Calculator", "Mark", "Mark down", "Packages" }, Titles(entries));
    }

    [TestMethod]
    public void OfTwoCommandsOnOneKeyOnlyTheOneThatTakesItIsListed()
    {
        var root = Tree((new Window(TestTheme()),
        [
            new TestCommand("Open folder", K(ConsoleKey.F3), canExecute: false),
            new TestCommand("View", K(ConsoleKey.F3)),
            new TestCommand("View file", K(ConsoleKey.F3)),
        ]));

        CollectionAssert.AreEqual(new[] { "View" }, Titles(CommandCatalog.Collect(root)));
    }

    [TestMethod]
    public void TheSameCommandInTwoPlacesIsListedOnce()
    {
        // The menu's Copy and the panel's Copy are the same entry to a reader.
        var root = Tree(
            (new Window(TestTheme()), [new TestCommand("Copy", K(ConsoleKey.F5))]),
            (new Window(TestTheme()), [new TestCommand("Copy", K(ConsoleKey.F5))]));

        Assert.AreEqual(1, CommandCatalog.Collect(root).Count);
    }

    [TestMethod]
    public void ADescriptionThatRepeatsTheTitleIsDropped()
    {
        var root = Tree((new Window(TestTheme()),
        [
            new TestCommand("Refresh", K(ConsoleKey.R, control: true)),
            new TestCommand("Find files", K(ConsoleKey.F7), description: "Search by name and content."),
        ]));

        var entries = CommandCatalog.Collect(root);

        Assert.AreEqual(string.Empty, entries.Single(e => e.Title == "Refresh").Description);
        Assert.AreEqual("Ctrl+R", entries.Single(e => e.Title == "Refresh").Key);
        Assert.AreEqual("Search by name and content.", entries.Single(e => e.Title == "Find files").Description);
    }

    private static readonly IReadOnlyList<PaletteEntry> Sample =
    [
        new(new TestCommand("Unit Converter", null), "Unit Converter", "Ctrl+E", "Length, mass, temperature."),
        new(new TestCommand("Calculator", null), "Calculator", "Ctrl+A", "Basic, scientific and programming modes."),
        new(new TestCommand("Copy", null), "Copy", "F5", "Copy the marked files."),
        new(new TestCommand("Compress", null), "Compress", "Alt+F5", "Pack files into an archive."),
        new(new TestCommand("Find files", null), "Find files", "F7", "Search by name; copy hits."),
    ];

    [TestMethod]
    public void EveryWordMustMatchSomewhereAndTitlesThatStartWithTheQueryLead()
    {
        CollectionAssert.AreEqual(new[] { "Compress", "Copy", "Unit Converter", "Find files" }, Titles(CommandCatalog.Filter(Sample, "co")),
            "titles starting with it (by name), then titles holding it, then descriptions holding it");
        CollectionAssert.AreEqual(new[] { "Compress" }, Titles(CommandCatalog.Filter(Sample, "alt f5")), "a key can be searched for");
        CollectionAssert.AreEqual(new[] { "Unit Converter" }, Titles(CommandCatalog.Filter(Sample, "converter unit")), "in any order");
        Assert.AreEqual(0, CommandCatalog.Filter(Sample, "nothing like this").Count);
    }

    [TestMethod]
    public void WithNoQueryTheCommandsRunLastComeFirst()
    {
        var ordered = CommandCatalog.Filter(Sample, "  ", ["Find files", "Copy"]);

        CollectionAssert.AreEqual(new[] { "Find files", "Copy", "Calculator", "Compress", "Unit Converter" }, Titles(ordered));
    }
}
