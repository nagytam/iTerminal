using iConsole.Display;
using iConsole.Drivers;

namespace iConsole.Test.Drivers;

[TestClass]
public class AnsiRendererTest
{
    [TestMethod]
    public void RenderCharacterWithColorsOnly()
    {
        var conxel = new Conxel('a', Color.DarkYellow, Color.Blue);
        var ansi = AnsiRenderer.Conxel2Ansi(1, 2, conxel).ToString();
        Assert.AreEqual("\x1b[3;2H\u001b[33;104ma\x1b[0m", ansi);
    }

    [TestMethod]
    public void RenderBoldCharacter()
    {
        var conxel = new Conxel('a', Color.DarkYellow, Color.Blue, true);
        var ansi = AnsiRenderer.Conxel2Ansi(1, 2, conxel).ToString();
        Assert.AreEqual("\x1b[3;2H\u001b[33;104;1ma\x1b[0m", ansi);
    }
}
