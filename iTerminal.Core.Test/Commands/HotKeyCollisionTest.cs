using iConsole.Data;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Two features on one chord would leave one of them unreachable: the chords of these global commands must differ.</summary>
[TestClass]
public class HotKeyCollisionTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TheDuplicateFinderAndTheSignaturesHaveDifferentChords()
    {
        var duplicates = new DuplicateFinderDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());
        var signatures = new SignatureDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());

        Assert.AreEqual(ConsoleKey.F6, duplicates.Key!.ConsoleKey);
        Assert.AreEqual(ConsoleKey.F7, signatures.Key!.ConsoleKey);
        Assert.AreNotEqual(duplicates.Key.ConsoleKey, signatures.Key.ConsoleKey);
    }

    /// <summary>
    /// Print is Ctrl+P: not Ctrl+Shift+P, which Windows Terminal keeps for its own command palette. Alt+P walks the command
    /// history, so the two must stay different chords.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void PrintIsPlainCtrlP_NotTheHostsOrTheHistorysChord()
    {
        var print = new PrintDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());

        Assert.AreEqual(new Key(ConsoleKey.P, false, false, true), print.Key);
        Assert.AreNotEqual(new Key(ConsoleKey.P, true, false, true), print.Key, "Ctrl+Shift+P is the terminal host's");
        Assert.AreNotEqual(new Key(ConsoleKey.P, false, true, false), print.Key, "Alt+P walks the command history");

    }
}
