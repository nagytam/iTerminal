using iConsole.Display;
using iTerminal.Core.ScreenSavers;

namespace iTerminal.Core.Test.ScreenSavers;

[TestClass]
public class ScreenSaverEffectsTest
{
    private static int CountCells(ScreenSaverCanvas canvas, Func<iConsole.Display.Conxel, bool> predicate)
    {
        int count = 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            foreach (var cell in canvas.Row(y))
            {
                if (predicate(cell))
                {
                    count++;
                }
            }
        }

        return count;
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Canvas_IgnoresWritesOutsideIt_AndRowsAreTheFullWidth()
    {
        var canvas = new ScreenSaverCanvas(10, 3);

        canvas.Put(-1, 0, '#', Color.White);
        canvas.Put(10, 0, '#', Color.White);
        canvas.Put(0, 3, '#', Color.White);
        canvas.Put(9, 2, '#', Color.White);

        Assert.AreEqual(10, canvas.Row(0).Count);
        Assert.AreEqual('#', canvas.Row(2)[9].Char);
        Assert.AreEqual(1, CountCells(canvas, c => c.Char == '#'), "Only the write on the canvas should land.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Starfield_PaintsStars_FrameAfterFrame()
    {
        var effect = new StarfieldEffect(new Random(1));
        var canvas = new ScreenSaverCanvas(80, 24);

        for (int frame = 0; frame < 120; frame++)
        {
            canvas.Clear();
            effect.Step(canvas, DateTime.Now);
        }

        Assert.IsTrue(CountCells(canvas, c => c.Char != ' ') > 0, "A starfield with no stars on screen after 120 frames.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Starfield_RebuildsItsStars_WhenTheWindowChangesSize()
    {
        var effect = new StarfieldEffect(new Random(2));
        effect.Step(new ScreenSaverCanvas(80, 24), DateTime.Now);

        var resized = new ScreenSaverCanvas(120, 40);
        effect.Step(resized, DateTime.Now);

        Assert.IsTrue(CountCells(resized, c => c.Char != ' ') > 0);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void MatrixRain_DrawsGreenTrails_WithBrightHeads()
    {
        var effect = new MatrixRainEffect(new Random(3));
        var canvas = new ScreenSaverCanvas(40, 20);

        for (int frame = 0; frame < 30; frame++)
        {
            canvas.Clear();
            effect.Step(canvas, DateTime.Now);
        }

        Assert.IsTrue(CountCells(canvas, c => c.ForegroundColor is Color.Green or Color.DarkGreen) > 0, "No green trail.");
        Assert.IsTrue(CountCells(canvas, c => c.ForegroundColor == Color.White && c.IsBold) > 0, "No bright head.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Clock_DrawsTheTime_InBlockCharacters()
    {
        var effect = new ClockEffect();
        var canvas = new ScreenSaverCanvas(80, 24);
        canvas.Clear();

        effect.Step(canvas, new DateTime(2026, 1, 1, 12, 34, 56));

        Assert.IsTrue(CountCells(canvas, c => c.Char == '█') > 0, "The clock drew no block characters.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Clock_DrawsNothing_OnACanvasTooSmallForIt()
    {
        var effect = new ClockEffect();
        var canvas = new ScreenSaverCanvas(20, 5);
        canvas.Clear();

        effect.Step(canvas, new DateTime(2026, 1, 1, 12, 34, 56));

        Assert.AreEqual(0, CountCells(canvas, c => c.Char != ' '));
    }
}
