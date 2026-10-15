using iConsole.Display;

namespace iConsole.Test.Display;

[TestClass]
public class ConxelStringTest
{
    [TestMethod]
    public void ConxelStringConstructorEmpty()
    {
        var conxelString = new ConxelString();
        Assert.AreEqual(0, conxelString.Count);
        Assert.IsFalse(conxelString.Any());
    }

    [TestMethod]
    public void ConxelStringConstructorWithCharacter()
    {
        var conxelString = new ConxelString('A', Color.White, Color.Black);
        Assert.AreEqual(1, conxelString.Count);
        Assert.AreEqual('A', conxelString[0].Char);
    }

    [TestMethod]
    public void ConxelStringConstructorWithString()
    {
        var conxelString = new ConxelString("Hello", Color.White, Color.Black);
        Assert.AreEqual(5, conxelString.Count);
        Assert.AreEqual("Hello", conxelString.Text);
    }

    [TestMethod]
    public void ConxelStringConstructorWithConxels()
    {
        var conxels = new[]
        {
            new Conxel('H', Color.White, Color.Black),
            new Conxel('i', Color.White, Color.Black)
        };
        var conxelString = new ConxelString(conxels);
        Assert.AreEqual(2, conxelString.Count);
        Assert.AreEqual("Hi", conxelString.Text);
    }

    [TestMethod]
    public void ConxelStringAppendConxel()
    {
        var conxelString = new ConxelString();
        var conxel = new Conxel('A', Color.White, Color.Black);
        conxelString.Append(conxel);
        Assert.AreEqual(1, conxelString.Count);
        Assert.AreEqual('A', conxelString[0].Char);
    }

    [TestMethod]
    public void ConxelStringAppendCharacter()
    {
        var conxelString = new ConxelString();
        conxelString.Append('A', Color.White, Color.Black);
        Assert.AreEqual(1, conxelString.Count);
        Assert.AreEqual('A', conxelString[0].Char);
    }

    [TestMethod]
    public void ConxelStringAppendCharacter_PreservesFormattingFlags()
    {
        var conxelString = new ConxelString();

        conxelString.Append('A', Color.Red, Color.Blue, true, true, true, true);

        Assert.AreEqual(1, conxelString.Count);
        Assert.AreEqual('A', conxelString[0].Char);
        Assert.IsTrue(conxelString[0].IsBold);
        Assert.IsTrue(conxelString[0].IsItalic);
        Assert.IsTrue(conxelString[0].IsUnderline);
        Assert.IsTrue(conxelString[0].IsStrikeThrough);
        Assert.AreEqual(Color.Red, conxelString[0].ForegroundColor);
        Assert.AreEqual(Color.Blue, conxelString[0].BackgroundColor);
    }

    [TestMethod]
    public void ConxelStringAppendString()
    {
        var conxelString = new ConxelString();
        conxelString.Append("Hello", Color.White, Color.Black);
        Assert.AreEqual(5, conxelString.Count);
        Assert.AreEqual("Hello", conxelString.Text);
    }

    [TestMethod]
    public void ConxelStringAppendConxelString()
    {
        var conxelString1 = new ConxelString("Hello", Color.White, Color.Black);
        var conxelString2 = new ConxelString(" World", Color.White, Color.Black);
        conxelString1.Append(conxelString2);
        Assert.AreEqual(11, conxelString1.Count);
        Assert.AreEqual("Hello World", conxelString1.Text);
    }

    [TestMethod]
    public void ConxelStringAppendNull()
    {
        var conxelString = new ConxelString("Hello", Color.White, Color.Black);
        conxelString.Append((string?)null, Color.White, Color.Black);
        Assert.AreEqual(5, conxelString.Count);
        Assert.AreEqual("Hello", conxelString.Text);
    }

    [TestMethod]
    public void ConxelStringExpand()
    {
        var conxelString = new ConxelString("Hi", Color.White, Color.Black);
        conxelString.Expand(5, Color.White, Color.Black);
        Assert.AreEqual(5, conxelString.Count);
        Assert.AreEqual("Hi   ", conxelString.Text);
    }

    [TestMethod]
    public void ConxelStringExpandWithFormatting()
    {
        var conxelString = new ConxelString("Hi", Color.Red, Color.Blue);
        conxelString.Expand(4, Color.Red, Color.Blue);
        Assert.AreEqual(4, conxelString.Count);
        Assert.AreEqual(Color.Red, conxelString[2].ForegroundColor);
        Assert.AreEqual(Color.Blue, conxelString[2].BackgroundColor);
    }

    [TestMethod]
    public void ConxelStringHighlightRange()
    {
        var conxelString = new ConxelString("Hello", Color.White, Color.Black);
        conxelString.HighlightRange(1, 4, Color.Yellow, Color.Red);
        Assert.AreEqual(Color.White, conxelString[0].ForegroundColor);
        Assert.AreEqual(Color.Yellow, conxelString[1].ForegroundColor);
        Assert.AreEqual(Color.Yellow, conxelString[2].ForegroundColor);
        Assert.AreEqual(Color.Yellow, conxelString[3].ForegroundColor);
        Assert.AreEqual(Color.White, conxelString[4].ForegroundColor);
    }

    [TestMethod]
    public void ConxelStringEnumerate()
    {
        var conxelString = new ConxelString("ABC", Color.White, Color.Black);
        int count = 0;
        foreach (var conxel in conxelString)
        {
            count++;
        }
        Assert.AreEqual(3, count);
    }

    [TestMethod]
    public void ConxelStringIndexer()
    {
        var conxelString = new ConxelString("ABC", Color.White, Color.Black);
        Assert.AreEqual('A', conxelString[0].Char);
        Assert.AreEqual('B', conxelString[1].Char);
        Assert.AreEqual('C', conxelString[2].Char);
    }

    [TestMethod]
    public void ConxelStringText()
    {
        var conxelString = new ConxelString("TestString", Color.White, Color.Black);
        Assert.AreEqual("TestString", conxelString.Text);
    }

    [TestMethod]
    public void ConxelStringWithFormatting()
    {
        var conxelString = new ConxelString("Bold", Color.White, Color.Black, true, false, false, false);
        Assert.IsTrue(conxelString[0].IsBold);
        Assert.IsFalse(conxelString[0].IsItalic);
    }

    [TestMethod]
    public void ConxelStringEmpty()
    {
        var conxelString = new ConxelString();
        Assert.IsFalse(conxelString.Any());
        conxelString.Append("test", Color.White, Color.Black);
        Assert.IsTrue(conxelString.Any());
    }

    [TestMethod]
    public void ConxelStringAppendString_PreservesFormattingFlags()
    {
        var conxelString = new ConxelString();

        conxelString.Append("Hi", Color.Red, Color.Blue, true, true, true, true);

        Assert.AreEqual(2, conxelString.Count);
        Assert.IsTrue(conxelString[0].IsBold);
        Assert.IsTrue(conxelString[0].IsItalic);
        Assert.IsTrue(conxelString[0].IsUnderline);
        Assert.IsTrue(conxelString[0].IsStrikeThrough);
        Assert.AreEqual(Color.Red, conxelString[0].ForegroundColor);
        Assert.AreEqual(Color.Blue, conxelString[0].BackgroundColor);
    }

    [TestMethod]
    public void ConxelStringAppendString_AppendsToExistingContent()
    {
        var conxelString = new ConxelString("A", Color.White, Color.Black);

        conxelString.Append("BC", Color.Yellow, Color.DarkBlue);

        Assert.AreEqual("ABC", conxelString.Text);
        Assert.AreEqual(Color.White, conxelString[0].ForegroundColor);
        Assert.AreEqual(Color.Yellow, conxelString[1].ForegroundColor);
        Assert.AreEqual(Color.Yellow, conxelString[2].ForegroundColor);
    }
}
