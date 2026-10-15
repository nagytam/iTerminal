using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Buffers;
using iConsole.Display;
using iConsole.Data;

namespace iConsole.Buffers.Test;

[TestClass]
public class BufferLayerTest
{
    [TestMethod]
    public void Clear_Write_Resize_AsString_Behavior()
    {
        var layer = new BufferLayer(0, new Size(5, 3), Color.White, Color.Black);

        layer.Write(new Position(1, 1), new Size(3, 1), new ConxelString("abc", Color.White, Color.Black));
        Assert.IsTrue(layer.AsString().Contains("abc"));

        layer.Clear();
        Assert.IsFalse(layer.AsString().Contains("abc"));

        layer.Resize(new Size(2, 2));
        Assert.AreEqual(2, layer.Size.Width);
        Assert.AreEqual(2, layer.Size.Height);
    }

    /// <summary>
    /// Controls pass their whole ViewSize for every row they write. The bounds used to grow by that
    /// size, so a list's last row stretched them a list's height below the dialog — and the layer is
    /// composited over everything below across its bounds.
    /// </summary>
    [TestMethod]
    public void TheBoundsGrowByWhatIsWrittenNotBySizeArgument()
    {
        var layer = new BufferLayer(1, new Size(20, 10), Color.White, Color.Black);

        layer.Write(new Position(2, 3), new Size(10, 5), new ConxelString("abcd", Color.White, Color.Black));

        Assert.AreEqual(2, layer.BoundRect!.Left);
        Assert.AreEqual(3, layer.BoundRect.Top);
        Assert.AreEqual(6, layer.BoundRect.Right, "four cells written");
        Assert.AreEqual(4, layer.BoundRect.Bottom, "one row written");
    }

    /// <summary>Two overlays on one layer: the cells between them belong to the layers below.</summary>
    [TestMethod]
    public void MergingCopiesOnlyTheCellsEachRowWrote()
    {
        var size = new Size(20, 3);
        var below = new BufferLayer(0, size, Color.White, Color.Black);
        for (uint y = 0; y < 3; y++)
        {
            below.Write(new Position(0, y), new Size(20, 1), new ConxelString(new string('.', 20), Color.White, Color.Black));
        }
        var overlay = new BufferLayer(1, size, Color.White, Color.Black);
        overlay.Write(new Position(0, 0), new Size(20, 1), new ConxelString("AA", Color.White, Color.Black));
        overlay.Write(new Position(15, 2), new Size(20, 1), new ConxelString("BB", Color.White, Color.Black));

        below.Merge(overlay, overlay.BoundRect!);

        var lines = below.AsString().Split(Environment.NewLine);
        Assert.AreEqual("AA" + new string('.', 18), lines[0].TrimEnd());
        Assert.AreEqual(new string('.', 20), lines[1].TrimEnd(), "a row the overlay never wrote is untouched");
        Assert.AreEqual(new string('.', 15) + "BB" + "...", lines[2].TrimEnd());
    }}
