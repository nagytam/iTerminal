using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Controls.MenuBar;

/// <summary>
/// Every menu entry that names a shortcut ("Copy\tF5") names it in a string of its own, apart from
/// the key its command is actually bound to. These tests hold the two together, so a rebinding that
/// forgets the menu shows up here instead of as a menu that teaches the wrong key.
/// </summary>
[TestClass]
public class MenuShortcutTest
{
    internal static IEnumerable<MenuItem> Menus()
    {
        var theme = TestTheme();
        var contentModelService = Mock.Of<IContentModelService>();
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());

        var compression = new iTerminal.Core.Services.Archives.ArchiveCompressionService(
            new iTerminal.Core.Test.Helper.FakeProcessRunner(),
            new iTerminal.Core.Services.Archives.SevenZipArchiveWriter(new iTerminal.Core.Test.Helper.FakeProcessRunner(), () => null));
        yield return new FileMenuItem(contentModelService, null!, null!, null!, null!, null!, theme,
            compressDialog: new CompressDialog(contentModelService, compression, null!, null!, Mock.Of<IBeepService>(),
                new DateTimeProviderService(), theme, null!, null!, null!, null!),
            extractDialog: new ExtractDialog(contentModelService,
                new iTerminal.Core.Services.Archives.ArchiveExtractionService(new iTerminal.Core.Test.Helper.FakeProcessRunner(), () => null),
                null!, null!, Mock.Of<IBeepService>(), new DateTimeProviderService(), theme, null!, null!, null!, null!),
            openWithDialogFactory: () => new OpenWithDialog(
                null!,
                new FileAssociationDialog(null!, Mock.Of<IBeepService>(), new DateTimeProviderService(), theme),
                theme),
            createLinkDialog: new CreateLinkDialog(null!, contentModelService, Mock.Of<IBeepService>(), new DateTimeProviderService(), theme),
            propertiesDialog: new PropertiesDialog(null!, contentModelService, Mock.Of<IBeepService>(), new DateTimeProviderService(), theme),
            checksumDialog: new ChecksumDialog(null!, Mock.Of<IBeepService>(), new DateTimeProviderService(), theme),
            printDialog: new PrintDialog(Mock.Of<iTerminal.Core.Services.Printing.IPrintService>(), Mock.Of<ISettingsService>(), theme));
        yield return new EditMenuItem(contentModelService, null!, null!, null!, null!, theme,
            new MarkPatternDialog(Mock.Of<IBeepService>(), new DateTimeProviderService(), theme),
            new MarkPatternDialog(Mock.Of<IBeepService>(), new DateTimeProviderService(), theme),
            new CompareFoldersDialog(contentModelService, new FileSystemResolver(null!, null!, null!, null!, contentModelService), theme));
        yield return new NavigateMenuItem(
            contentModelService, null!, new DriveInfoModel(), null!, Mock.Of<IBeepService>(), new DateTimeProviderService(),
            new SettingsService(), guard.Object, null!, theme,
            shellCommand: new iTerminal.Core.Commands.InteractiveShellCommand(contentModelService, Mock.Of<iTerminal.Core.Services.Shell.IInteractiveShellService>()),
            sortDialog: new SortDialog(new SettingsService(), theme));
        yield return new ToolsMenuItem(null!, null!, null!, null!, contentModelService, null!, null!, null!, null!, null!, theme);
        yield return new AboutMenuItem(null!, null!, theme,
            new CommandPaletteDialog(new SettingsService(), Mock.Of<IBeepService>(), new DateTimeProviderService(), theme));
    }

    internal static string Describe(Key key)
    {
        var parts = new List<string>();
        if (key.Control)
        {
            parts.Add("Ctrl");
        }

        if (key.Alt)
        {
            parts.Add("Alt");
        }

        if (key.Shift)
        {
            parts.Add("Shift");
        }

        parts.Add(key.ConsoleKey.ToString());
        return string.Join('+', parts);
    }

    /// <summary>A chord with its modifiers in a fixed order, so "Ctrl+Alt+X" and "Alt+Ctrl+X" compare equal.</summary>
    private static string Normalize(string chord)
    {
        var parts = chord.Split('+');
        var modifiers = parts[..^1].Select(p => p.ToLowerInvariant()).Order();
        return string.Join('+', modifiers.Append(parts[^1].ToLowerInvariant()));
    }

    private static IEnumerable<MenuItem> Entries(MenuItem menu) =>
        menu.Controls.AsSnapshot().OfType<MenuItem>().Where(m => m.Text != "-");

    [TestMethod]
    public void EveryAdvertisedShortcutIsTheKeyTheCommandIsBoundTo()
    {
        var mismatches = new List<string>();
        foreach (var menu in Menus())
        {
            foreach (var entry in Entries(menu))
            {
                var tab = entry.Text.IndexOf('\t');
                if (tab < 0)
                {
                    continue;
                }

                var advertised = entry.Text[(tab + 1)..];
                var command = entry.Commands.FirstOrDefault();
                // KeyDisplayText lists every key the command answers to ("Ctrl+Shift+M / Ctrl+F7").
                var bound = command?.KeyDisplayText.Split(" / ", StringSplitOptions.RemoveEmptyEntries) ?? [];
                if (!bound.Any(b => Normalize(b) == Normalize(advertised)))
                {
                    var actual = bound.Length == 0 ? "(none)" : string.Join(" / ", bound);
                    mismatches.Add($"{menu.Text} > {entry.Text[..tab]}: menu says {advertised}, command is bound to {actual}");
                }
            }
        }

        Assert.AreEqual(0, mismatches.Count, string.Join(Environment.NewLine, mismatches));
    }

    [TestMethod]
    public void EveryEntryWithABoundKeyAdvertisesIt()
    {
        var silent = new List<string>();
        foreach (var menu in Menus())
        {
            foreach (var entry in Entries(menu))
            {
                if (!entry.Text.Contains('\t') && entry.Commands.FirstOrDefault()?.Key is { } key)
                {
                    silent.Add($"{menu.Text} > {entry.Text} (bound to {Describe(key)})");
                }
            }
        }

        Assert.AreEqual(0, silent.Count, string.Join(Environment.NewLine, silent));
    }

    [TestMethod]
    public void EveryEntryRunsSomething()
    {
        foreach (var menu in Menus())
        {
            foreach (var entry in Entries(menu))
            {
                Assert.IsNotNull(entry.Commands.FirstOrDefault(), $"{menu.Text} > {entry.Text} has no command");
            }
        }
    }

    [TestMethod]
    public void TheMenusAreInTheirUsualOrder()
    {
        CollectionAssert.AreEqual(
            new[] { "File", "Edit", "Navigate", "Tools", "Help" },
            Menus().Select(m => m.Text).ToArray());
    }
}
