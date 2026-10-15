using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Dialogs.Test;

[TestClass]
public class HelpDialogTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private sealed class TestCommand(string title, string description, Key key, bool canExecute = true) : Command
    {
        public override string Title => title;
        public override string Description => description;
        public override Key? Key { get; init; } = key;

        public override bool CanExecute()
        {
            return canExecute;
        }

        public override Task ExecuteAsync(object? args)
        {
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public void Constructor_Default_ShouldCreateInstance()
    {
        var themeService = CreateThemeService();

        var dialog = new HelpDialog(themeService);

        Assert.IsNotNull(dialog);
    }

    [TestMethod]
    public async Task OnInitializeAsync_ListsVisibleExecutableCommandsWithDescriptions()
    {
        var themeService = CreateThemeService();
        var dialog = new HelpDialog(themeService);

        var root = new Control(themeService) { Name = "Root" };
        var focusedPanel = new Control(themeService) { Name = "Focused panel", CanFocus = true };
        root.AddControl(focusedPanel);

        focusedPanel.AddCommand(new TestCommand(
            title: "Open item",
            description: "Open the selected item.",
            key: new Key(ConsoleKey.Enter, false, false, false)));

        root.AddCommand(new TestCommand(
            title: "Refresh",
            description: "Reload the current content.",
            key: new Key(ConsoleKey.R, false, false, true)));

        focusedPanel.AddCommand(new TestCommand(
            title: "Unavailable",
            description: "This command should be filtered out.",
            key: new Key(ConsoleKey.F6, false, false, false),
            canExecute: false));

        var hiddenPanel = new Control(themeService) { Name = "Hidden panel", IsVisible = false };
        hiddenPanel.AddCommand(new TestCommand(
            title: "Hidden",
            description: "This command should not be shown.",
            key: new Key(ConsoleKey.F7, false, false, false)));
        root.AddControl(hiddenPanel);

        dialog.ContextControl = root;
        dialog.ContextFocus = focusedPanel;

        await dialog.OnInitializeAsync();

        var textViewer = dialog.Controls.OfType<TextViewerControl>().Single();
        var helpText = string.Join(Environment.NewLine, Available(textViewer.TextModel.Lines));

        StringAssert.Contains(helpText, "Focused control: Focused panel");
        StringAssert.Contains(helpText, "Enter              Open item");
        StringAssert.Contains(helpText, "Open the selected item.");
        StringAssert.Contains(helpText, "Ctrl+R             Refresh");
        StringAssert.Contains(helpText, "Reload the current content.");
        Assert.IsFalse(helpText.Contains("Unavailable", StringComparison.Ordinal));
        Assert.IsFalse(helpText.Contains("Hidden", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OnInitializeAsync_IncludesMenuNavigationSectionWhenMenuBarPresent()
    {
        var themeService = CreateThemeService();
        var dialog = new HelpDialog(themeService);

        var root = new Control(themeService) { Name = "Root" };
        var fileMenu = new MenuItem("File", null, [], themeService) { HotKeyChar = 'F' };
        var menuBar = new MenuBar([fileMenu], themeService);
        root.AddControl(menuBar);

        dialog.ContextControl = root;
        dialog.ContextFocus = root;

        await dialog.OnInitializeAsync();

        var textViewer = dialog.Controls.OfType<TextViewerControl>().Single();
        var helpText = string.Join(Environment.NewLine, textViewer.TextModel.Lines);

        StringAssert.Contains(helpText, "Menu navigation:");
        StringAssert.Contains(helpText, "Ctrl+F");
        StringAssert.Contains(helpText, "Open File menu");
    }

    /// <summary>The part of the help that lists what can run in this context, before the complete list.</summary>
    private static IEnumerable<string> Available(IEnumerable<string> lines) =>
        lines.TakeWhile(l => !l.StartsWith("All commands in the application:", StringComparison.Ordinal));

    /// <summary>Every command of the application is listed, including the hidden ones and those that cannot run here.</summary>
    [TestMethod]
    public async Task TheCompleteListHoldsEveryCommandEvenWhenItCannotRunOrIsHidden()
    {
        var themeService = CreateThemeService();
        var dialog = new HelpDialog(themeService);
        var root = new Control(themeService) { Name = "Root" };
        root.AddCommand(new TestCommand("Open or run", "Open or run the file.", new Key(ConsoleKey.Enter, false, false, false), canExecute: false));
        var hidden = new Control(themeService) { Name = "Hidden panel", IsVisible = false };
        hidden.AddCommand(new TestCommand("Packages", "Manage packages.", new Key(ConsoleKey.F8, false, true, false)));
        root.AddControl(hidden);
        dialog.ContextControl = root;
        dialog.ContextFocus = root;

        await dialog.OnInitializeAsync();

        var lines = dialog.Controls.OfType<TextViewerControl>().Single().TextModel.Lines;
        var complete = lines.SkipWhile(l => !l.StartsWith("All commands in the application:", StringComparison.Ordinal)).ToList();
        Assert.IsTrue(complete.Any(l => l.Contains("Open or run")), string.Join("\n", lines));
        Assert.IsTrue(complete.Any(l => l.Contains("Packages") && l.Contains("Alt+F8")), string.Join("\n", lines));
        Assert.IsFalse(Available(lines).Any(l => l.Contains("Open or run")));
    }

    private static async Task<string[]> HelpLinesAsync(params TestCommand[] commands)
    {
        var themeService = CreateThemeService();
        var dialog = new HelpDialog(themeService);
        var root = new Control(themeService) { Name = "Root" };
        foreach (var command in commands)
        {
            root.AddCommand(command);
        }
        dialog.ContextControl = root;
        dialog.ContextFocus = root;
        await dialog.OnInitializeAsync();
        return [.. Available(dialog.Controls.OfType<TextViewerControl>().Single().TextModel.Lines)];
    }

    /// <summary>A plain string sort listed Alt+F11 and Alt+F12 before Alt+F9, and Ctrl before plain keys.</summary>
    [TestMethod]
    public async Task KeysAreListedPlainFirstAndFunctionKeysByNumber()
    {
        var lines = await HelpLinesAsync(
            new TestCommand("Eleven", "e", new Key(ConsoleKey.F11, false, true, false)),
            new TestCommand("Nine", "n", new Key(ConsoleKey.F9, false, true, false)),
            new TestCommand("Two", "t", new Key(ConsoleKey.F2, false, false, false)),
            new TestCommand("Ctrl two", "c", new Key(ConsoleKey.F2, false, false, true)));

        var order = lines.Where(l => l.StartsWith("  ", StringComparison.Ordinal) && !l.StartsWith("   ", StringComparison.Ordinal))
            .Select(l => l.Trim().Split(' ')[0]).ToList();
        CollectionAssert.AreEqual(new[] { "F2", "Ctrl+F2", "Alt+F9", "Alt+F11" }, order);
    }

    [TestMethod]
    public async Task ADescriptionThatRepeatsTheTitleIsLeftOut()
    {
        var lines = await HelpLinesAsync(new TestCommand("Toggle Terminal", "Toggle Terminal", new Key(ConsoleKey.O, false, false, true)));

        Assert.AreEqual(1, lines.Count(l => l.Contains("Toggle Terminal")), string.Join("\n", lines));
    }

    [TestMethod]
    public async Task AShortKeyKeepsItsTitleOnItsLine()
    {
        var lines = await HelpLinesAsync(new TestCommand("Music player", "Open the music panels.", new Key(ConsoleKey.F7, false, false, true)));

        Assert.IsTrue(lines.Any(l => l.StartsWith("  Ctrl+F7", StringComparison.Ordinal) && l.Contains("Music player")));
    }

    /// <summary>Of two runnable commands on one key, the first takes the key — only it is listed.</summary>
    [TestMethod]
    public async Task OnlyTheCommandThatWouldRunIsListedForASharedKey()
    {
        var enter = new Key(ConsoleKey.Enter, false, false, false);
        var lines = await HelpLinesAsync(
            new TestCommand("Open folder", "Open the folder.", enter),
            new TestCommand("Open or run", "Open or run the file.", enter));

        Assert.IsTrue(lines.Any(l => l.Contains("Open folder")));
        Assert.IsFalse(lines.Any(l => l.Contains("Open or run")), string.Join("\n", lines));
    }

    [TestMethod]
    public async Task ACommandThatCannotRunDoesNotHideTheOneThatCan()
    {
        var enter = new Key(ConsoleKey.Enter, false, false, false);
        var lines = await HelpLinesAsync(
            new TestCommand("Open folder", "Open the folder.", enter, canExecute: false),
            new TestCommand("Open or run", "Open or run the file.", enter));

        Assert.IsTrue(lines.Any(l => l.Contains("Open or run")));
    }
}