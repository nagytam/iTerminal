using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class SpinnerTest
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private static Mock<IThemeService> ThemeMock()
    {
        var m = new Mock<IThemeService>();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m;
    }

    /// <summary>A settable fake clock so OnIdle's elapsed-time gate can be advanced deterministically in tests.</summary>
    private sealed class FakeDateTimeProviderService : IDateTimeProviderService
    {
        public DateTime Now { get; set; } = new(2024, 1, 1);
        public DateTime UtcNow => Now;
    }

    [TestMethod]
    public void Constructor_DefaultsToActive()
    {
        var spinner = new Spinner(ThemeMock().Object, new FakeDateTimeProviderService());

        Assert.IsTrue(spinner.IsActive);
    }

    [TestMethod]
    public void Constructor_DefaultLabelIsEmpty()
    {
        var spinner = new Spinner(ThemeMock().Object, new FakeDateTimeProviderService());

        Assert.AreEqual(string.Empty, spinner.Label);
    }

    [TestMethod]
    public void Draw_RendersFrameCharacter()
    {
        var buffer = new MockBuffer(new Size(10, 1));
        var spinner = new Spinner(ThemeMock().Object, new FakeDateTimeProviderService())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        spinner.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(Frames.Any(f => content.Contains(f)), "Expected a spinner frame character");
    }

    [TestMethod]
    public void Draw_WithLabel_RendersLabelAfterFrame()
    {
        var buffer = new MockBuffer(new Size(20, 1));
        var spinner = new Spinner(ThemeMock().Object, new FakeDateTimeProviderService())
        {
            Label = "Working",
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        spinner.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Working"));
    }

    [TestMethod]
    public void Draw_Inactive_ShowsSpaceInsteadOfFrame()
    {
        var buffer = new MockBuffer(new Size(10, 1));
        var spinner = new Spinner(ThemeMock().Object, new FakeDateTimeProviderService())
        {
            IsActive = false,
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        spinner.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsFalse(Frames.Any(f => content.Contains(f)), "Inactive spinner must not render a frame character");
    }

    [TestMethod]
    public void Draw_Inactive_WithLabel_StillRendersLabel()
    {
        var buffer = new MockBuffer(new Size(20, 1));
        var spinner = new Spinner(ThemeMock().Object, new FakeDateTimeProviderService())
        {
            IsActive = false,
            Label = "Done",
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        spinner.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Done"));
    }

    [TestMethod]
    public void Draw_PadsOutputToViewWidth()
    {
        var buffer = new MockBuffer(new Size(15, 1));
        var spinner = new Spinner(ThemeMock().Object, new FakeDateTimeProviderService())
        {
            Label = "Hi",
            Position = new Position(0, 0),
            ViewSize = new Size(15, 1)
        };

        spinner.Draw(buffer);

        var line = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.AreEqual(15, line.Length);
    }

    [TestMethod]
    public void OnIdle_BeforeFrameIntervalElapses_DoesNotAdvanceFrame()
    {
        var buffer1 = new MockBuffer(new Size(5, 1));
        var buffer2 = new MockBuffer(new Size(5, 1));
        var clock = new FakeDateTimeProviderService();
        var spinner = new Spinner(ThemeMock().Object, clock)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(5, 1)
        };

        spinner.Draw(buffer1);
        clock.Now += spinner.FrameInterval - TimeSpan.FromMilliseconds(1);
        spinner.OnIdle();
        spinner.Draw(buffer2);

        Assert.AreEqual(buffer1.ContentAsString, buffer2.ContentAsString,
            "OnIdle fires far more often than the frame should change; it must not advance before FrameInterval elapses");
    }

    [TestMethod]
    public void OnIdle_Active_AdvancesFrameAfterIntervalElapses()
    {
        var buffer1 = new MockBuffer(new Size(5, 1));
        var buffer2 = new MockBuffer(new Size(5, 1));
        var clock = new FakeDateTimeProviderService();
        var spinner = new Spinner(ThemeMock().Object, clock)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(5, 1)
        };

        spinner.Draw(buffer1);
        clock.Now += spinner.FrameInterval;
        spinner.OnIdle();
        spinner.Draw(buffer2);

        // Frame must have advanced; the two draws must differ
        var first = buffer1.ContentAsString[..1];
        var second = buffer2.ContentAsString[..1];
        Assert.AreNotEqual(first, second, "Frame should advance once FrameInterval has elapsed");
    }

    [TestMethod]
    public void OnIdle_Inactive_DoesNotAdvanceFrame()
    {
        var buffer1 = new MockBuffer(new Size(5, 1));
        var buffer2 = new MockBuffer(new Size(5, 1));
        var clock = new FakeDateTimeProviderService();
        var spinner = new Spinner(ThemeMock().Object, clock)
        {
            IsActive = false,
            Position = new Position(0, 0),
            ViewSize = new Size(5, 1)
        };

        spinner.Draw(buffer1);
        clock.Now += spinner.FrameInterval * 5;
        spinner.OnIdle();
        spinner.Draw(buffer2);

        Assert.AreEqual(buffer1.ContentAsString, buffer2.ContentAsString);
    }

    [TestMethod]
    public void OnIdle_CyclesAllTenFrames()
    {
        var clock = new FakeDateTimeProviderService();
        var spinner = new Spinner(ThemeMock().Object, clock)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(5, 1)
        };

        var seen = new HashSet<string>();
        for (var i = 0; i < Frames.Length; i++)
        {
            var buffer = new MockBuffer(new Size(5, 1));
            spinner.Draw(buffer);
            seen.Add(buffer.ContentAsString[..1]);
            clock.Now += spinner.FrameInterval;
            spinner.OnIdle();
        }

        Assert.AreEqual(Frames.Length, seen.Count, "All 10 frames should appear exactly once per cycle");
    }

    [TestMethod]
    public void OnIdle_WrapsAroundAfterLastFrame()
    {
        var clock = new FakeDateTimeProviderService();
        var spinner = new Spinner(ThemeMock().Object, clock)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(5, 1)
        };

        // Advance through all 10 frames
        for (var i = 0; i < Frames.Length; i++)
        {
            clock.Now += spinner.FrameInterval;
            spinner.OnIdle();
        }

        var bufferAfterWrap = new MockBuffer(new Size(5, 1));
        spinner.Draw(bufferAfterWrap);

        // After 10 OnIdle calls we're back to frame index 10 % 10 == 0, but Draw happens
        // before the first OnIdle so the initial index was 0. After 10 advances it's 0 again.
        Assert.AreEqual(Frames[0], bufferAfterWrap.ContentAsString[..1]);
    }
}
