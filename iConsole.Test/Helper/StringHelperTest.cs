using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class StringHelperTest
{
    [TestMethod]
    public void StringRepeatString()
    {
        var result = "abc".Repeat(3);
        Assert.AreEqual("abcabcabc", result);
    }

    [TestMethod]
    public void StringRepeatStringZero()
    {
        var result = "abc".Repeat(0);
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void StringRepeatStringNegative()
    {
        var result = "abc".Repeat(-1);
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void StringRepeatChar()
    {
        var result = StringHelper.Repeat('a', 5);
        Assert.AreEqual("aaaaa", result);
    }

    [TestMethod]
    public void StringRepeatCharZero()
    {
        var result = StringHelper.Repeat('a', 0);
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void StringCut()
    {
        var result = "Hello World".Cut(5);
        Assert.AreEqual("Hello", result);
    }

    [TestMethod]
    public void StringCutExact()
    {
        var result = "Hello".Cut(5);
        Assert.AreEqual("Hello", result);
    }

    [TestMethod]
    public void StringCutSmaller()
    {
        var result = "Hi".Cut(5);
        Assert.AreEqual("Hi", result);
    }

    [TestMethod]
    public void StringCutZero()
    {
        var result = "Hello".Cut(0);
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void StringExpandLeftRight()
    {
        var result = "Hi".Expand(2, 2);
        Assert.AreEqual("  Hi  ", result);
    }

    [TestMethod]
    public void StringExpandWidth()
    {
        var result = "Hi".Expand(10);
        Assert.IsTrue(result.Length == 10);
        Assert.IsTrue(result.Contains("Hi"));
    }

    [TestMethod]
    public void StringExpandWidthWithChar()
    {
        var result = "Hi".Expand("=", 10);
        Assert.AreEqual(10, result.Length);
        Assert.IsTrue(result.Contains("Hi"));
    }

    [TestMethod]
    public void StringExpandRight()
    {
        var result = "Hi".ExpandRight(10);
        Assert.AreEqual("Hi        ", result);
        Assert.AreEqual(10, result.Length);
    }

    [TestMethod]
    public void StringShrinkNormal()
    {
        var result = "Hello World".Shrink(7);
        Assert.AreEqual(7, result.Length);
        Assert.IsTrue(result.Contains("..."));
    }

    [TestMethod]
    public void StringShrinkNull()
    {
        string? nullString = null;
        var result = nullString.Shrink(10);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void StringShrinkEmpty()
    {
        var result = string.Empty.Shrink(10);
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void StringShrinkNoTrim()
    {
        var result = "Short".Shrink(10);
        Assert.AreEqual("Short", result);
    }

    [TestMethod]
    public void StringAlignCenter()
    {
        var result = StringHelper.AlignCenter("=========", "ABC");
        Assert.IsTrue(result.Contains("ABC"));
        Assert.AreEqual(9, result.Length);
    }

    [TestMethod]
    public void StringToMemoryStream()
    {
        var result = "Hello".ToMemoryStream(System.Text.Encoding.UTF8);
        Assert.IsNotNull(result);
        var content = System.Text.Encoding.UTF8.GetString(result.ToArray());
        // UTF8 StreamWriter includes BOM by default, so content will start with BOM
        Assert.IsTrue(content.Contains("Hello"));
    }

    [TestMethod]
    public void WrapWordsBreaksAtSpaces()
    {
        CollectionAssert.AreEqual(new[] { "alpha beta", "gamma" }, "alpha beta gamma".WrapWords(10));
    }

    [TestMethod]
    public void WrapWordsBreaksAWordLongerThanTheLine()
    {
        CollectionAssert.AreEqual(new[] { "abcd", "efgh", "ij" }, "abcdefghij".WrapWords(4));
    }

    [TestMethod]
    public void WrapWordsKeepsBlankLinesAndDropsCarriageReturns()
    {
        var text = string.Join(Environment.NewLine, "one", string.Empty, "two");

        CollectionAssert.AreEqual(new[] { "one", "", "two" }, text.WrapWords(20));
    }

    [TestMethod]
    public void WrapWordsOfNothingIsNothing()
    {
        Assert.AreEqual(0, ((string?)null).WrapWords(10).Count);
        Assert.AreEqual(0, "text".WrapWords(0).Count);
    }

    [TestMethod]
    public void FitHints_LeavesARowThatFitsAlone()
    {
        Assert.AreEqual("Enter:open  Esc:close", "Enter:open  Esc:close".FitHints(40));
    }

    /// <summary>Whole hints go, from the end — but the last one, how to leave, stays.</summary>
    [TestMethod]
    public void FitHints_DropsWholeHintsAndKeepsTheLast()
    {
        var fitted = "Tab:next list  Up/Down:choose  type a value  Ctrl+S:swap units  Esc:close".FitHints(40);

        Assert.AreEqual("Tab:next list  Up/Down:choose  Esc:close", fitted);
    }

    [TestMethod]
    public void FitHints_CutsOnlyWhenOneHintIsTooLongOnItsOwn()
    {
        Assert.AreEqual("Esc:cl", "Enter:open  Esc:close".FitHints(6));
    }}
