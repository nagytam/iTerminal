using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class MeterTest
{
    private static Mock<IThemeService> ThemeMock()
    {
        var m = new Mock<IThemeService>();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m;
    }

    private static Meter CreateMeter(double value = 0.0, int width = 10)
    {
        return new Meter(ThemeMock().Object)
        {
            Value = value,
            Position = new Position(0, 0),
            ViewSize = new Size(width, 1)
        };
    }

    // ── Value clamping ─────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Value_SetAbove1_ClampsTo1()
    {
        var meter = CreateMeter();
        meter.Value = 1.5;
        Assert.AreEqual(1.0, meter.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Value_SetBelow0_ClampsTo0()
    {
        var meter = CreateMeter();
        meter.Value = -0.5;
        Assert.AreEqual(0.0, meter.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Value_SetExactBoundaries_Accepted()
    {
        var meter = CreateMeter();
        meter.Value = 0.0;
        Assert.AreEqual(0.0, meter.Value);
        meter.Value = 1.0;
        Assert.AreEqual(1.0, meter.Value);
    }

    // ── Render at 0 % ──────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_At0Percent_AllEmptyChars()
    {
        var buffer = new MockBuffer(new Size(10, 1));
        CreateMeter(0.0, 10).Draw(buffer);

        var line = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.AreEqual("░░░░░░░░░░", line);
    }

    // ── Render at 100 % ────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_At100Percent_AllFillChars()
    {
        var buffer = new MockBuffer(new Size(10, 1));
        CreateMeter(1.0, 10).Draw(buffer);

        var line = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.AreEqual("██████████", line);
    }

    // ── Render at 50 % ─────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_At50Percent_HalfFilledHalfEmpty()
    {
        var buffer = new MockBuffer(new Size(10, 1));
        CreateMeter(0.5, 10).Draw(buffer);

        var line = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.AreEqual("█████░░░░░", line);
    }

    // ── Output length ──────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_OutputWidthMatchesViewSize()
    {
        var buffer = new MockBuffer(new Size(20, 1));
        var meter = new Meter(ThemeMock().Object)
        {
            Value = 0.3,
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        meter.Draw(buffer);

        var line = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.AreEqual(20, line.Length);
    }

    // ── Custom fill / empty chars ───────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_CustomFillChar_UsedForFilledPortion()
    {
        var buffer = new MockBuffer(new Size(10, 1));
        var meter = new Meter(ThemeMock().Object)
        {
            Value = 1.0,
            FillChar = '#',
            EmptyChar = '-',
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        meter.Draw(buffer);

        Assert.AreEqual("##########", buffer.ContentAsString.Split(Environment.NewLine)[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_CustomEmptyChar_UsedForEmptyPortion()
    {
        var buffer = new MockBuffer(new Size(10, 1));
        var meter = new Meter(ThemeMock().Object)
        {
            Value = 0.0,
            FillChar = '#',
            EmptyChar = '-',
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        meter.Draw(buffer);

        Assert.AreEqual("----------", buffer.ContentAsString.Split(Environment.NewLine)[0]);
    }

    // ── Label ──────────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_WithLabel_LabelAppearsBeforeBar()
    {
        var buffer = new MockBuffer(new Size(15, 1));
        var meter = new Meter(ThemeMock().Object)
        {
            Value = 0.0,
            Label = "CPU",
            Position = new Position(0, 0),
            ViewSize = new Size(15, 1)
        };

        meter.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.StartsWith("CPU ░"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_WithLabel_BarFillsRemainingWidth()
    {
        // "CPU " is 4 chars; bar occupies the remaining 11 chars of width 15
        var buffer = new MockBuffer(new Size(15, 1));
        var meter = new Meter(ThemeMock().Object)
        {
            Value = 1.0,
            Label = "CPU",
            Position = new Position(0, 0),
            ViewSize = new Size(15, 1)
        };

        meter.Draw(buffer);

        var line = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.AreEqual("CPU " + "███████████", line);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_NoLabel_EntireWidthIsBar()
    {
        var buffer = new MockBuffer(new Size(8, 1));
        var meter = new Meter(ThemeMock().Object)
        {
            Value = 1.0,
            Label = string.Empty,
            Position = new Position(0, 0),
            ViewSize = new Size(8, 1)
        };

        meter.Draw(buffer);

        Assert.AreEqual("████████", buffer.ContentAsString.Split(Environment.NewLine)[0]);
    }

    // ── Disabled state ─────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_DisabledState_DoesNotThrow()
    {
        var buffer = new MockBuffer(new Size(10, 1));
        var meter = new Meter(ThemeMock().Object)
        {
            Value = 0.5,
            IsEnabled = false,
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        meter.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }
}
