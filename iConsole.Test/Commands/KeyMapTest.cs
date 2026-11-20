using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Model;
using Moq;

namespace iConsole.Test.Commands;

[TestClass]
public class KeyMapTest
{
    private static BackspaceCommand CreateCommand()
    {
        var mockScrollableModel = new Mock<IScrollableModel>();
        var mockTextModel = new Mock<ITextModel>();
        return new BackspaceCommand(mockTextModel.Object, mockScrollableModel.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnmappedCommandKeepsItsDefaultKey()
    {
        using var scope = KeyMap.Scope(new Dictionary<string, Key?>());
        var command = CreateCommand();

        Assert.AreSame(command.Key, command.ActiveKey);
        Assert.AreEqual(Command.FormatKey(command.Key), command.KeyDisplayText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMappedCommandAnswersToItsMappedKey()
    {
        var mapped = new Key(ConsoleKey.F5, true, false, true);
        using var scope = KeyMap.Scope(new Dictionary<string, Key?> { [typeof(BackspaceCommand).FullName!] = mapped });
        var command = CreateCommand();

        Assert.AreSame(mapped, command.ActiveKey);
        Assert.AreEqual("Ctrl+Shift+F5", command.KeyDisplayText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANullBindingRemovesTheKey()
    {
        using var scope = KeyMap.Scope(new Dictionary<string, Key?> { [typeof(BackspaceCommand).FullName!] = null });
        var command = CreateCommand();

        Assert.IsNull(command.ActiveKey);
        Assert.AreEqual(string.Empty, command.KeyDisplayText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheScopeEndsWhenItIsDisposed()
    {
        var mapped = new Key(ConsoleKey.F5, true, false, true);
        var command = CreateCommand();
        Key? defaultKey = command.Key;

        using (KeyMap.Scope(new Dictionary<string, Key?> { [typeof(BackspaceCommand).FullName!] = mapped }))
        {
            Assert.AreSame(mapped, command.ActiveKey);
        }

        Assert.AreSame(defaultKey, command.ActiveKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACommandRunsOnItsMappedKeyOnly()
    {
        var command = CreateCommand();
        Key? defaultKey = command.Key;
        Assert.IsNotNull(defaultKey);
        var mapped = new Key(ConsoleKey.F5, true, false, true);

        using var scope = KeyMap.Scope(new Dictionary<string, Key?> { [typeof(BackspaceCommand).FullName!] = mapped });

        var mappedEvent = new KeyEvent('\0', ConsoleKey.F5, true, false, true);
        var defaultEvent = new KeyEvent('\b', defaultKey.ConsoleKey, defaultKey.Shift, defaultKey.Alt, defaultKey.Control);
        Assert.IsTrue(await command.HandleKeyEventAsync(mappedEvent), "the mapped key runs the command");
        Assert.IsFalse(await command.HandleKeyEventAsync(defaultEvent), "the default key no longer does");
    }
}
