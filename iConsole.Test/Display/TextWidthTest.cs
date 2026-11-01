using iConsole.Display;

namespace iConsole.Test.Display;

[TestClass]
public class TextWidthTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Of_CountsColumns_WideCharactersAsTwo()
    {
        Assert.AreEqual(3, TextWidth.Of("abc"));
        Assert.AreEqual(4, TextWidth.Of("日本"));
        Assert.AreEqual(2, TextWidth.Of("😀"));
        Assert.AreEqual(4, TextWidth.Of("a😀b"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Of_DropsZeroWidthCharacters()
    {
        // 'e' followed by a combining acute accent is one column.
        Assert.AreEqual(1, TextWidth.Of("é"));
        // A zero-width joiner between two emoji adds no column.
        Assert.AreEqual(4, TextWidth.Of("😀‍😀"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Of_HalfwidthKatakana_IsOneColumn()
    {
        Assert.AreEqual(1, TextWidth.Of("ｱ"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToCells_FollowsAWideBmpCharacterWithAContinuation()
    {
        var cells = TextWidth.ToCells("日a").ToArray();

        CollectionAssert.AreEqual(new[] { '日', TextWidth.Continuation, 'a' }, cells);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToCells_KeepsAnEmojiAsItsSurrogatePair()
    {
        var cells = TextWidth.ToCells("😀").ToArray();

        Assert.AreEqual(2, cells.Length);
        Assert.IsTrue(char.IsHighSurrogate(cells[0]));
        Assert.IsTrue(char.IsLowSurrogate(cells[1]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToCells_ShowsANarrowCharacterOutsideTheBmpAsAReplacement()
    {
        // MATHEMATICAL BOLD CAPITAL A: outside the BMP and one column wide, so it has no single cell.
        var cells = TextWidth.ToCells("\U0001D400").ToArray();

        CollectionAssert.AreEqual(new[] { TextWidth.Replacement }, cells);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FromCells_ReadsBackWhatToCellsWrote()
    {
        const string text = "日本😀x";

        Assert.AreEqual(text, TextWidth.FromCells(TextWidth.ToCells(text)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Fit_CutsWithoutSplittingAWideCharacter()
    {
        Assert.AreEqual("日本 ", TextWidth.Fit("日本語", 5));
        Assert.AreEqual("日本", TextWidth.Fit("日本語", 4));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Fit_PadsShortTextToTheWidth()
    {
        Assert.AreEqual("abc  ", TextWidth.Fit("abc", 5));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NeedsCellLayout_IsFalseForPlainLatin()
    {
        Assert.IsFalse(TextWidth.NeedsCellLayout("plain text, é included"));
        Assert.IsTrue(TextWidth.NeedsCellLayout("日本"));
        Assert.IsTrue(TextWidth.NeedsCellLayout("é"));
    }
}
