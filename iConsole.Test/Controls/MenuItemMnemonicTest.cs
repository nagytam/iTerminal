using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>
/// A menu's Ctrl+letter mnemonic must not take a chord that a command is bound to: the Edit menu's
/// Ctrl+E used to shadow the unit converter, whose button and menu entry both advertised Ctrl+E.
/// </summary>
[TestClass]
public class MenuItemMnemonicTest
{
    private sealed class RecordingCommand(Key key) : Command
    {
        public int Runs { get; private set; }
        public override Key? Key { get; init; } = key;

        public override Task ExecuteAsync(object? args)
        {
            Runs++;
            return Task.CompletedTask;
        }
    }

    private static (Control Root, MenuItem Edit) CreateScreen(params ICommand[] boundCommands)
    {
        var theme = new TestThemeService();
        var edit = new MenuItem("Edit", null, [], theme) { HotKeyChar = 'E' };
        var menuBar = new iConsole.Controls.MenuBar([edit], theme);
        var root = new Control(theme) { ViewSize = new Size(80, 25) };
        var hotKeys = new Control(theme);
        foreach (var command in boundCommands)
        {
            hotKeys.AddCommand(command);
        }
        root.AddControl(menuBar);
        root.AddControl(hotKeys);
        return (root, edit);
    }

    private static KeyEvent CtrlE => new('\u0005', ConsoleKey.E, false, false, true);

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNothingBoundToTheChord_TheMnemonicOpensTheMenu()
    {
        var (root, edit) = CreateScreen();

        Assert.IsTrue(await root.HandleKeyEventAsync(CtrlE));

        Assert.IsTrue(edit.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACommandBoundToTheChord_WinsOverTheMnemonic()
    {
        var converter = new RecordingCommand(new Key(ConsoleKey.E, false, false, true));
        var (root, edit) = CreateScreen(converter);

        Assert.IsTrue(await root.HandleKeyEventAsync(CtrlE));

        Assert.AreEqual(1, converter.Runs);
        Assert.IsFalse(edit.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACommandOnAnotherChord_LeavesTheMnemonicAlone()
    {
        var other = new RecordingCommand(new Key(ConsoleKey.E, false, true, false));
        var (root, edit) = CreateScreen(other);

        await root.HandleKeyEventAsync(CtrlE);

        Assert.IsTrue(edit.IsOpened);
        Assert.AreEqual(0, other.Runs);
    }
}
