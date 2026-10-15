using iConsole.Display;

namespace iConsole.Test.Display;

[TestClass]
public class ConxelTest
{
    [TestMethod]
    public void ConxelConstructor()
    {
        var conxel = new Conxel('A', Color.White, Color.Black);
        Assert.AreEqual('A', conxel.Char);
        Assert.AreEqual(Color.White, conxel.ForegroundColor);
        Assert.AreEqual(Color.Black, conxel.BackgroundColor);
        Assert.IsFalse(conxel.IsBold);
        Assert.IsFalse(conxel.IsItalic);
        Assert.IsFalse(conxel.IsUnderline);
        Assert.IsFalse(conxel.IsStrikeThrough);
    }

    [TestMethod]
    public void ConxelConstructorWithFormatting()
    {
        var conxel = new Conxel('A', Color.White, Color.Black, true, true, true, true);
        Assert.AreEqual('A', conxel.Char);
        Assert.IsTrue(conxel.IsBold);
        Assert.IsTrue(conxel.IsItalic);
        Assert.IsTrue(conxel.IsUnderline);
        Assert.IsTrue(conxel.IsStrikeThrough);
    }

    [TestMethod]
    public void ConxelProperties()
    {
        var conxel = new Conxel('X', Color.Red, Color.Yellow);
        conxel.Char = 'Y';
        Assert.AreEqual('Y', conxel.Char);

        conxel.IsBold = true;
        Assert.IsTrue(conxel.IsBold);

        conxel.ForegroundColor = Color.Green;
        Assert.AreEqual(Color.Green, conxel.ForegroundColor);
    }

    [TestMethod]
    public void ConxelEquality()
    {
        var conxel1 = new Conxel('A', Color.White, Color.Black, true, false, false, false);
        var conxel2 = new Conxel('A', Color.White, Color.Black, true, false, false, false);
        Assert.AreEqual(conxel1, conxel2);
    }

    [TestMethod]
    public void ConxelInequality()
    {
        var conxel1 = new Conxel('A', Color.White, Color.Black);
        var conxel2 = new Conxel('B', Color.White, Color.Black);
        Assert.AreNotEqual(conxel1, conxel2);
    }

    [TestMethod]
    public void ConxelWithDifferentColors()
    {
        var conxel = new Conxel('A', Color.Cyan, Color.Magenta);
        Assert.AreEqual(Color.Cyan, conxel.ForegroundColor);
        Assert.AreEqual(Color.Magenta, conxel.BackgroundColor);
    }
}
