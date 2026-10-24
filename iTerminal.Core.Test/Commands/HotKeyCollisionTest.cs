using iConsole.Data;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Two features on one chord would leave one of them unreachable: the chords of these global commands must differ.</summary>
[TestClass]
public class HotKeyCollisionTest
{
    [TestMethod]
    public void TheDuplicateFinderAndTheSignaturesHaveDifferentChords()
    {
        var duplicates = new DuplicateFinderDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());
        var signatures = new SignatureDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());

        Assert.AreEqual(ConsoleKey.F6, duplicates.Key!.ConsoleKey);
        Assert.AreEqual(ConsoleKey.F7, signatures.Key!.ConsoleKey);
        Assert.AreNotEqual(duplicates.Key.ConsoleKey, signatures.Key.ConsoleKey);
    }
}
