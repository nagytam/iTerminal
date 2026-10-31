using iConsole.Localization;

namespace iConsole.Test.Localization;

/// <summary>A hot key is drawn in its caption: the preferred letter when the caption has it, else a letter of the caption's last word.</summary>
[TestClass]
public class HotKeysTest
{
    [TestMethod]
    public void VisibleIn_KeepsThePreferredLetterWhenTheCaptionHasIt()
    {
        Assert.AreEqual('D', HotKeys.VisibleIn("Delete", 'D'));
        Assert.AreEqual('c', HotKeys.VisibleIn("Cancel", 'c'), "the case of the preferred letter is kept");
        Assert.AreEqual('F', HotKeys.VisibleIn("Create folder", 'F'), "a letter the caption has is returned as given");
    }

    [TestMethod]
    public void VisibleIn_TakesTheLastWordsFirstLetterWhenTheCaptionLacksThePreferredOne()
    {
        Assert.AreEqual('L', HotKeys.VisibleIn("Löschen", 'D'));
        Assert.AreEqual('e', HotKeys.VisibleIn("Ordner erstellen", 'F'));
        Assert.AreEqual('v', HotKeys.VisibleIn("In den Papierkorb verschieben", 'M'));
        Assert.AreEqual('A', HotKeys.VisibleIn("Annuler", 'c'));
    }

    [TestMethod]
    public void VisibleIn_SkipsPunctuationAndDigitsWhenLookingForALetter()
    {
        Assert.AreEqual('b', HotKeys.VisibleIn("123 ... bis", 'x'));
    }

    [TestMethod]
    public void VisibleIn_ReturnsThePreferredLetterWhenTheCaptionHasNoLetter()
    {
        Assert.AreEqual('x', HotKeys.VisibleIn(string.Empty, 'x'));
        Assert.AreEqual('x', HotKeys.VisibleIn("   ", 'x'));
        Assert.AreEqual('x', HotKeys.VisibleIn("123 ...", 'x'));
    }
}
