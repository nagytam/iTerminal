using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;

namespace iConsole.Test.Display;

[TestClass]
public class WideCharacterCellsTest
{
    [TestMethod]
    public void ConxelString_GivesAWideCharacterTwoCells()
    {
        var text = new ConxelString("日本", Color.White, Color.Black);

        Assert.AreEqual(4, text.Count);
        Assert.AreEqual("日本", text.Text);
    }

    [TestMethod]
    public void ConxelString_GivesAnEmojiTwoCells()
    {
        var text = new ConxelString("a😀", Color.White, Color.Black);

        Assert.AreEqual(3, text.Count);
        Assert.AreEqual("a😀", text.Text);
    }

    [TestMethod]
    public void RepairWideCharacters_BlanksAHalfThatLostItsPartner()
    {
        var layer = new BufferLayer(0, new Size(5, 1), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(2, 1), new ConxelString("日", Color.White, Color.Black));

        // A narrow character drawn over the second column of the wide one.
        layer.Write(new Position(1, 0), new Size(1, 1), new ConxelString("x", Color.White, Color.Black));
        layer.RepairWideCharacters();

        Assert.AreEqual(' ', layer[0].Span[0].Char);
        Assert.AreEqual('x', layer[0].Span[1].Char);
    }

    [TestMethod]
    public void RepairWideCharacters_LeavesAWhollyWideCharacterAlone()
    {
        var layer = new BufferLayer(0, new Size(4, 1), Color.White, Color.Black);
        layer.Write(new Position(0, 0), new Size(4, 1), new ConxelString("日本", Color.White, Color.Black));

        layer.RepairWideCharacters();

        Assert.AreEqual("日本", TextWidth.FromCells(layer[0].Span.ToArray().Select(c => c.Char)));
    }

    [TestMethod]
    public void AnsiOutput_NeverWritesTheContinuationCell()
    {
        var output = AnsiRenderer.ConxelString2Ansi(0, 0, new ConxelString("日本", Color.White, Color.Black));

        StringAssert.Contains(output, "日本");
        Assert.IsFalse(output.Contains(TextWidth.Continuation));
    }
}
