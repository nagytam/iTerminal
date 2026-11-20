using System.Text.Json;
using iConsole.Commands;
using iConsole.Data;
using iTerminal.Core.Commands;
using iTerminal.Core.Services.KeyBindings;

namespace iTerminal.Core.Test.Services.KeyBindings;

[TestClass]
public class KeyMapFileTest
{
    private static readonly Type[] Commands =
    [
        typeof(CopyCommand),
        typeof(BackspaceCommand),
        typeof(KeyBindingsFirst.ProbeCommand),
        typeof(KeyBindingsSecond.ProbeCommand),
    ];

    private static string Bindings(string entries) => "{ \"bindings\": { " + entries + " } }";

    [Timeout(60_000)]
    [TestMethod]
    public void AShortCommandNameBindsItsKey()
    {
        KeyMapParseResult result = KeyMapFile.Parse(Bindings("\"CopyCommand\": \"Ctrl+F5\""), Commands);

        Assert.AreEqual(0, result.Problems.Count);
        Key? key = result.Bindings[typeof(CopyCommand).FullName!];
        Assert.IsNotNull(key);
        Assert.AreEqual(ConsoleKey.F5, key.ConsoleKey);
        Assert.IsTrue(key.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFullCommandNameAlsoBinds()
    {
        KeyMapParseResult result = KeyMapFile.Parse(
            Bindings($"\"{typeof(BackspaceCommand).FullName}\": \"Alt+F7\""), Commands);

        Assert.AreEqual(0, result.Problems.Count);
        Assert.IsTrue(result.Bindings.ContainsKey(typeof(BackspaceCommand).FullName!));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoneRemovesTheKey()
    {
        KeyMapParseResult result = KeyMapFile.Parse(Bindings("\"CopyCommand\": \"none\""), Commands);

        Assert.AreEqual(0, result.Problems.Count);
        Assert.IsTrue(result.Bindings.ContainsKey(typeof(CopyCommand).FullName!));
        Assert.IsNull(result.Bindings[typeof(CopyCommand).FullName!]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnAmbiguousShortNameIsReportedWithTheFullNames()
    {
        KeyMapParseResult result = KeyMapFile.Parse(Bindings("\"ProbeCommand\": \"F6\""), Commands);

        Assert.AreEqual(0, result.Bindings.Count);
        Assert.AreEqual(1, result.Problems.Count);
        StringAssert.Contains(result.Problems[0].Reason, "KeyBindingsFirst.ProbeCommand");
        StringAssert.Contains(result.Problems[0].Reason, "KeyBindingsSecond.ProbeCommand");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnknownCommandIsReportedAndTheOthersStillApply()
    {
        KeyMapParseResult result = KeyMapFile.Parse(
            Bindings("\"NoSuchCommand\": \"F6\", \"CopyCommand\": \"Ctrl+F5\""), Commands);

        Assert.AreEqual(1, result.Bindings.Count);
        Assert.AreEqual(1, result.Problems.Count);
        Assert.AreEqual("NoSuchCommand", result.Problems[0].Binding);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("\"CopyCommand\": \"Ctrl+M\"")]
    [DataRow("\"CopyCommand\": \"A\"")]
    [DataRow("\"CopyCommand\": \"Alt+A\"")]
    [DataRow("\"CopyCommand\": \"Hyper+F5\"")]
    [DataRow("\"CopyCommand\": 5")]
    public void AnUnusableBindingIsReportedAndNotApplied(string entry)
    {
        KeyMapParseResult result = KeyMapFile.Parse(Bindings(entry), Commands);

        Assert.AreEqual(0, result.Bindings.Count);
        Assert.AreEqual(1, result.Problems.Count);
        Assert.AreEqual("CopyCommand", result.Problems[0].Binding);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFirstOfTwoCommandsOnOneKeyKeepsIt()
    {
        KeyMapParseResult result = KeyMapFile.Parse(
            Bindings("\"CopyCommand\": \"Ctrl+F5\", \"BackspaceCommand\": \"ctrl+f5\""), Commands);

        Assert.IsTrue(result.Bindings.ContainsKey(typeof(CopyCommand).FullName!));
        Assert.IsFalse(result.Bindings.ContainsKey(typeof(BackspaceCommand).FullName!));
        Assert.AreEqual(1, result.Problems.Count);
        Assert.AreEqual("BackspaceCommand", result.Problems[0].Binding);
        StringAssert.Contains(result.Problems[0].Reason, "already bound");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ACommandBoundTwiceKeepsTheFirstBinding()
    {
        KeyMapParseResult result = KeyMapFile.Parse(
            Bindings("\"CopyCommand\": \"Ctrl+F5\", \"CopyCommand\": \"Ctrl+F6\""), Commands);

        Assert.AreEqual(Command.FormatKey(new Key(ConsoleKey.F5, false, false, true)),
            Command.FormatKey(result.Bindings[typeof(CopyCommand).FullName!]));
        Assert.AreEqual(1, result.Problems.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        string json = """
            {
              // the copy key moves out of the way
              "bindings": {
                "CopyCommand": "Ctrl+F5",
              },
            }
            """;

        KeyMapParseResult result = KeyMapFile.Parse(json, Commands);

        Assert.AreEqual(0, result.Problems.Count);
        Assert.AreEqual(1, result.Bindings.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("{ \"bindings\": [] }")]
    [DataRow("{ \"bindings\": ")]
    public void AFileThatIsNotAKeyMapIsRefused(string json)
    {
        Assert.Throws<JsonException>(() => KeyMapFile.Parse(json, Commands));
    }
}
