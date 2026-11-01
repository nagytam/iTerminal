using iTerminal.Core.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// The Create button of the Create file and Create folder dialogs takes its keyboard letter from its own caption, so the
/// letter is always drawn in the caption, in every language, and never collides with Cancel's C or the name box's N.
/// </summary>
[TestClass]
public class CreateEntryDialogTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void CreateButtonHotKey_IsTheFirstLetterOfTheLastWord_ForTheEnglishCaptions()
    {
        Assert.AreEqual('f', CreateEntryDialog.CreateButtonHotKey("Create folder"));
        Assert.AreEqual('f', CreateEntryDialog.CreateButtonHotKey("Create file"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateButtonHotKey_IsDrawnInTheCaption_ForTheTranslatedCaptions()
    {
        // The highlight only shows when the letter is in the caption, so each translation must contain its letter.
        foreach (string caption in new[] { "Ordner erstellen", "Datei erstellen", "Créer un dossier", "Crear archivo", "Crea file", "Fájl létrehozása" })
        {
            char? hotKey = CreateEntryDialog.CreateButtonHotKey(caption);
            Assert.IsNotNull(hotKey, caption);
            StringAssert.Contains(caption.ToLowerInvariant(), hotKey.Value.ToString(), caption);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateButtonHotKey_SkipsTheLettersCancelAndTheNameBoxHold()
    {
        Assert.AreEqual('a', CreateEntryDialog.CreateButtonHotKey("Crear carpeta"), "the C of carpeta is Cancel's");
        Assert.AreEqual('a', CreateEntryDialog.CreateButtonHotKey("Crea cartella"), "the C of cartella is Cancel's");
        Assert.AreEqual('a', CreateEntryDialog.CreateButtonHotKey("Cancel"), "the C of Cancel is Cancel's");
        Assert.IsNull(CreateEntryDialog.CreateButtonHotKey("Nc"), "only N and C: both are taken");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateButtonHotKey_IsNullWhenTheCaptionHasNoLetter()
    {
        Assert.IsNull(CreateEntryDialog.CreateButtonHotKey(string.Empty));
        Assert.IsNull(CreateEntryDialog.CreateButtonHotKey("   "));
        Assert.IsNull(CreateEntryDialog.CreateButtonHotKey("123 ..."));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateButtonHotKey_IgnoresCase()
    {
        Assert.AreEqual('F', CreateEntryDialog.CreateButtonHotKey("CREATE FOLDER"));
    }
}
