using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class KeyValueListTest
{
    private readonly TestThemeService _theme = new();

    [TestMethod]
    public void NullKeyOrValue_DrawsAsAnEmptyCell_InsteadOfFailing()
    {
        var list = new KeyValueList(_theme) { Position = new Position(0, 0), ViewSize = new Size(20, 2) };

        list.SetItems([(null!, "x"), ("Name", null!)]);
        var screen = ScreenBuffer.Render(list, 20, 2);

        Assert.AreEqual("      x", screen.Lines[0]);
        Assert.AreEqual("Name", screen.Lines[1]);
    }

    [TestMethod]
    public void Draw_ShowsKeysAndValuesInColumns()
    {
        var list = new KeyValueList(_theme) { Position = new Position(0, 0), ViewSize = new Size(20, 2) };

        list.SetItems([("Size", "12 KB"), ("Owner", "tamas")]);
        var screen = ScreenBuffer.Render(list, 20, 2);

        Assert.AreEqual("Size   12 KB", screen.Lines[0]);
        Assert.AreEqual("Owner  tamas", screen.Lines[1]);
    }
}
