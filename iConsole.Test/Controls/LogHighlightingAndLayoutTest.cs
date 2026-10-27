using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Layouts;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class LogHighlightingAndLayoutTest
{
    private readonly TestThemeService _theme = new();

    // ---- ANSI ----

    [TestMethod]
    public void Ansi_ColoursBecomeSpans_AndTheCodesLeaveTheText()
    {
        (string plain, IReadOnlyList<LogSpan> spans) = AnsiText.Parse("ok \u001b[31mfailed\u001b[0m done");

        Assert.AreEqual("ok failed done", plain);
        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual(3, spans[0].Start);
        Assert.AreEqual(6, spans[0].Length);
        Assert.AreEqual(Color.DarkRed, spans[0].Foreground);
    }

    [TestMethod]
    public void Ansi_BoldBrightBackgroundAndUnderline_CombineInOneRun()
    {
        (string plain, IReadOnlyList<LogSpan> spans) = AnsiText.Parse("\u001b[1;4;92;44mgo\u001b[0m");

        Assert.AreEqual("go", plain);
        Assert.IsTrue(spans[0].Bold && spans[0].Underline);
        Assert.AreEqual(Color.Green, spans[0].Foreground);
        Assert.AreEqual(Color.DarkBlue, spans[0].Background);
    }

    [TestMethod]
    public void Ansi_256AndTrueColour_MapToTheNearestOfSixteen()
    {
        (_, IReadOnlyList<LogSpan> indexed) = AnsiText.Parse("\u001b[38;5;196mred\u001b[0m");
        (_, IReadOnlyList<LogSpan> rgb) = AnsiText.Parse("\u001b[38;2;250;5;5mred\u001b[0m");
        (_, IReadOnlyList<LogSpan> gray) = AnsiText.Parse("\u001b[48;5;244mg\u001b[0m");

        Assert.AreEqual(Color.Red, indexed[0].Foreground);
        Assert.AreEqual(Color.Red, rgb[0].Foreground);
        Assert.AreEqual(Color.DarkGray, gray[0].Background);
    }

    [TestMethod]
    public void Ansi_OtherSequencesAreDropped_AndAnUnfinishedOneDoesNotThrow()
    {
        (string plain, IReadOnlyList<LogSpan> spans) = AnsiText.Parse("a\u001b[2Kb\u001b[1;1Hc\u001b]0;title\u0007d\u001b]8;;http://x\u001b\\e\u001b[");

        Assert.AreEqual("abcde", plain);
        Assert.AreEqual(0, spans.Count);
        Assert.AreEqual("plain", AnsiText.Parse("plain").Plain);
    }

    [TestMethod]
    public void Ansi_ResetWithNoNumberAndOffCodesClearTheStyle()
    {
        (string plain, IReadOnlyList<LogSpan> spans) = AnsiText.Parse("\u001b[31;1mA\u001b[mB\u001b[32mC\u001b[39mD\u001b[1mE\u001b[22mF");

        Assert.AreEqual("ABCDEF", plain);
        Assert.AreEqual(3, spans.Count);
        Assert.AreEqual(Color.DarkRed, spans[0].Foreground);
        Assert.AreEqual(Color.DarkGreen, spans[1].Foreground);
        Assert.IsTrue(spans[2].Bold);
    }

    // ---- highlighters ----

    private static string[] Found(string line) =>
        [.. LogHighlighters.Find(line, LogHighlighters.Default, []).OrderBy(s => s.Start).Select(s => line.Substring(s.Start, s.Length))];

    [TestMethod]
    public void TheDefaultHighlighters_FindUrlsPathsNumbersTimestampsAndErrorWords()
    {
        CollectionAssert.AreEqual(
            new[] { "2026-10-28 12:00:01", "ERROR", "https://example.com/a?b=1", "42" },
            Found("2026-10-28 12:00:01 ERROR see https://example.com/a?b=1 code 42"));
        CollectionAssert.AreEqual(new[] { @"C:\src\app.cs:12" }, Found(@"at C:\src\app.cs:12"));
        CollectionAssert.AreEqual(new[] { "/var/log/app.log:7" }, Found("in /var/log/app.log:7"));
        CollectionAssert.AreEqual(new[] { "\"quoted text\"" }, Found("said \"quoted text\""));
        CollectionAssert.AreEqual(new[] { "WARN" }, Found("WARN  low disk"));
    }

    [TestMethod]
    public void ANumberInsideAWordOrAUrl_IsLeftAlone()
    {
        CollectionAssert.AreEqual(Array.Empty<string>(), Found("abc123 v2.0.1 id4"));
        CollectionAssert.AreEqual(new[] { "http://x.org/a/12/b" }, Found("http://x.org/a/12/b"));
    }

    [TestMethod]
    public void WhereTwoRulesOverlap_TheEarlierOneWins_AndAnsiSpansAreNotCovered()
    {
        string line = "FAILED at https://x/y";

        LogSpan[] spans = [.. LogHighlighters.Find(line, LogHighlighters.Default, [new LogSpan(0, 6, Color.Blue)])];

        Assert.AreEqual(1, spans.Length, "the error word is under an ANSI colour, so only the URL is added");
        Assert.AreEqual(10, spans[0].Start);
    }

    [TestMethod]
    public void AVeryLongLine_IsNotHighlighted()
    {
        string line = new string('1', LogHighlighters.MaxLineLength + 1);

        Assert.AreEqual(0, LogHighlighters.Find(line, LogHighlighters.Default, []).Count());
    }

    // ---- the log view ----

    private static ScreenBuffer Draw(LogView view)
    {
        var screen = new ScreenBuffer((int)view.ViewSize.Width, (int)view.ViewSize.Height);
        view.Draw(screen);
        view.Draw(screen);
        return screen;
    }

    private LogView NewView(int width = 60, int height = 4) => new(_theme) { ViewSize = new Size(width, height) };

    [TestMethod]
    public void TheLogView_DrawsHighlightedPartsInTheirColours()
    {
        LogView view = NewView();
        view.Append("see https://example.com now");

        ScreenBuffer screen = Draw(view);

        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(5, 0));
        Assert.IsTrue(screen.Cell(5, 0).IsUnderline);
        Assert.AreEqual(new ColorScheme().Foreground, screen.ForegroundAt(0, 0));
        Assert.AreEqual(new ColorScheme().Foreground, screen.ForegroundAt(30, 0));
    }

    [TestMethod]
    public void TheLogView_ReadsAnsiColours_AndTheTextHasNoEscapes()
    {
        LogView view = NewView();
        view.Append("build \u001b[1;32mpassed\u001b[0m in 3s");

        ScreenBuffer screen = Draw(view);

        Assert.AreEqual("build passed in 3s", screen.Line(0).TrimEnd());
        Assert.AreEqual(Color.DarkGreen, screen.ForegroundAt(7, 0));
        Assert.IsTrue(screen.Cell(7, 0).IsBold);
        Assert.AreEqual("build passed in 3s", view.VisibleEntries[0].Text);
    }

    [TestMethod]
    public void AColourStaysOnAcrossTheLinesOfOneAppend()
    {
        LogView view = NewView();
        view.Append("\u001b[33mfirst\nsecond\u001b[0m\nthird");

        ScreenBuffer screen = Draw(view);

        Assert.AreEqual(Color.DarkYellow, screen.ForegroundAt(0, 0));
        Assert.AreEqual(Color.DarkYellow, screen.ForegroundAt(0, 1));
        Assert.AreEqual(new ColorScheme().Foreground, screen.ForegroundAt(0, 2));
        CollectionAssert.AreEqual(new[] { "first", "second", "third" }, view.VisibleEntries.Select(e => e.Text).ToArray());
    }

    [TestMethod]
    public void ParseAnsiOff_StripsTheCodesButDoesNotColour()
    {
        LogView view = NewView();
        view.ParseAnsi = false;
        view.HighlightingEnabled = false;
        view.Append("a \u001b[31mred\u001b[0m b");

        ScreenBuffer screen = Draw(view);

        Assert.AreEqual("a red b", screen.Line(0).TrimEnd());
        Assert.AreEqual(new ColorScheme().Foreground, screen.ForegroundAt(3, 0));
    }

    [TestMethod]
    public void HighlightingOff_LeavesTheLevelColour_AndCustomHighlightersReplaceTheDefaults()
    {
        LogView view = NewView();
        view.HighlightingEnabled = false;
        view.Append("error 42");
        Assert.AreEqual(new ColorScheme().Foreground, Draw(view).ForegroundAt(6, 0));

        LogView custom = NewView();
        custom.Highlighters = [LogHighlighter.Create("foo", Color.Blue, bold: true)];
        custom.Append("a foo 42");
        ScreenBuffer screen = Draw(custom);
        Assert.AreEqual(Color.Blue, screen.ForegroundAt(2, 0));
        Assert.AreEqual(new ColorScheme().Foreground, screen.ForegroundAt(6, 0), "the number rule was replaced");
    }

    [TestMethod]
    public void Highlights_FollowSidewaysScrolling()
    {
        LogView view = NewView(20, 3);
        view.Append("0123456789 https://example.com/x");
        view.Append("short");
        Draw(view);

        view.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.RightArrow, false, false, false)).Wait();
        view.SetFocus();
        view.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.RightArrow, false, false, false)).Wait();
        ScreenBuffer screen = Draw(view);

        Assert.IsTrue(Enumerable.Range(0, 20).Any(x => screen.ForegroundAt(x, 0) == Color.Cyan));
    }

    [TestMethod]
    public void LinesAreUnchanged_ByHighlightingTheyAreStillFiltersAndCounts()
    {
        LogView view = NewView();
        view.Append("see https://example.com now", LogEntryLevel.Warning);
        view.Append("plain", LogEntryLevel.Debug);

        view.MinimumLevel = LogEntryLevel.Warning;

        Assert.AreEqual(1, view.VisibleEntries.Count);
        Assert.IsNotNull(view.VisibleEntries[0].Spans);
        view.FilterText = "EXAMPLE";
        Assert.AreEqual(1, view.VisibleEntries.Count);
    }

    // ---- alignment in stacks ----

    private Control Child(int width, int height) => new(_theme) { ViewSize = new Size(width, height) };

    private StackPanel Stack(Orientation orientation, int width, int height, StackAlignment cross = StackAlignment.Stretch) =>
        new(_theme) { Orientation = orientation, ViewSize = new Size(width, height), CrossAlignment = cross };

    [TestMethod]
    public void ByDefault_ChildrenStretchAcrossTheAxis()
    {
        StackPanel panel = Stack(Orientation.Vertical, 20, 10);
        Control child = Child(5, 3);
        panel.Add(child, SizeRule.Fixed(3));

        Assert.AreEqual(new Size(20, 3), child.ViewSize);
        Assert.AreEqual(new Position(0, 0), child.Position);
    }

    [TestMethod]
    [DataRow(StackAlignment.Start, 0)]
    [DataRow(StackAlignment.Center, 7)]
    [DataRow(StackAlignment.End, 15)]
    public void InAColumn_AChildKeepsItsWidthAndSitsAtTheStartMiddleOrEnd(StackAlignment alignment, int expectedX)
    {
        StackPanel panel = Stack(Orientation.Vertical, 20, 10, alignment);
        Control child = Child(5, 3);

        panel.Add(child, SizeRule.Fixed(3));

        Assert.AreEqual(new Size(5, 3), child.ViewSize);
        Assert.AreEqual(new Position(expectedX, 0), child.Position);
    }

    [TestMethod]
    public void InARow_TheCrossAxisIsTheHeight()
    {
        StackPanel panel = Stack(Orientation.Horizontal, 30, 9);
        Control top = Child(4, 2);
        Control middle = Child(4, 3);
        Control bottom = Child(4, 4);
        panel.Add(top, SizeRule.Fixed(4), StackAlignment.Start);
        panel.Add(middle, SizeRule.Fixed(4), StackAlignment.Center);
        panel.Add(bottom, SizeRule.Fixed(4), StackAlignment.End);

        Assert.AreEqual(new Position(0, 0), top.Position);
        Assert.AreEqual(new Position(4, 3), middle.Position);
        Assert.AreEqual(new Position(8, 5), bottom.Position);
        Assert.AreEqual(new Size(4, 3), middle.ViewSize);
    }

    [TestMethod]
    public void AChildWiderThanThePanel_IsHeldToIt_AndSetAlignmentCanTakeASize()
    {
        StackPanel panel = Stack(Orientation.Vertical, 10, 6);
        Control wide = Child(40, 2);
        Control later = Child(9, 2);
        panel.Add(wide, SizeRule.Fixed(2), StackAlignment.End);
        panel.Add(later, SizeRule.Fixed(2));

        Assert.AreEqual(new Size(10, 2), wide.ViewSize);

        panel.SetAlignment(later, StackAlignment.Center, size: 4);
        Assert.AreEqual(new Size(4, 2), later.ViewSize);
        Assert.AreEqual(3, later.Position.X);

        panel.SetAlignment(later, null);
        Assert.AreEqual(new Size(10, 2), later.ViewSize, "back to the panel's own alignment, which stretches");
    }

    [TestMethod]
    [DataRow(StackJustify.Start, new[] { 0, 3, 6 })]
    [DataRow(StackJustify.Center, new[] { 6, 9, 12 })]
    [DataRow(StackJustify.End, new[] { 12, 15, 18 })]
    [DataRow(StackJustify.SpaceBetween, new[] { 0, 9, 18 })]
    [DataRow(StackJustify.SpaceAround, new[] { 2, 9, 16 })]
    [DataRow(StackJustify.SpaceEvenly, new[] { 3, 9, 15 })]
    public void Justify_SharesOutTheRoomAlongTheAxis(StackJustify justify, int[] expected)
    {
        StackPanel panel = Stack(Orientation.Horizontal, 21, 1);
        panel.Justify = justify;
        Control[] children = [Child(3, 1), Child(3, 1), Child(3, 1)];
        foreach (Control child in children)
        {
            panel.Add(child, SizeRule.Fixed(3));
        }

        CollectionAssert.AreEqual(expected, children.Select(c => (int)c.Position.X).ToArray());
    }

    [TestMethod]
    public void WithAFillChild_ThereIsNoRoomLeftToShare()
    {
        StackPanel panel = Stack(Orientation.Horizontal, 21, 1);
        panel.Justify = StackJustify.End;
        Control fixedChild = Child(3, 1);
        Control fill = Child(3, 1);
        panel.Add(fixedChild, SizeRule.Fixed(3));
        panel.Add(fill);

        Assert.AreEqual(0, fixedChild.Position.X);
        Assert.AreEqual(18, fill.ViewSize.Width);
    }

    [TestMethod]
    public void JustifyAndGap_WorkTogether_AndAHiddenChildTakesNoSpace()
    {
        StackPanel panel = Stack(Orientation.Horizontal, 20, 1);
        panel.Gap = 2;
        panel.Justify = StackJustify.Center;
        Control a = Child(3, 1);
        Control hidden = Child(3, 1);
        Control b = Child(3, 1);
        panel.Add(a, SizeRule.Fixed(3));
        panel.Add(hidden, SizeRule.Fixed(3));
        panel.Add(b, SizeRule.Fixed(3));
        hidden.IsVisible = false;
        panel.OnInitializeLayout();

        Assert.AreEqual(6, a.Position.X);
        Assert.AreEqual(11, b.Position.X);
    }

    [TestMethod]
    public void Distribute_NeverReturnsNegativeOrMissingRoom()
    {
        (int first, int[] gaps) = StackPanel.Distribute(StackJustify.SpaceBetween, 5, 1, 1);
        Assert.AreEqual(0, first);
        Assert.AreEqual(1, gaps.Length);

        (int none, int[] zero) = StackPanel.Distribute(StackJustify.SpaceAround, 5, 0, 1);
        Assert.AreEqual(0, none);
        Assert.AreEqual(0, zero.Length);

        (int tight, _) = StackPanel.Distribute(StackJustify.Center, -4, 3, 1);
        Assert.AreEqual(0, tight);
    }

    // ---- wrapper views ----

    [TestMethod]
    public void AlignPanel_CentresItsContentAtItsOwnSize()
    {
        var panel = new AlignPanel(_theme) { ViewSize = new Size(21, 9) };
        Control content = Child(7, 3);

        panel.SetContent(content);

        Assert.AreEqual(new Position(7, 3), content.Position);
        Assert.AreEqual(new Size(7, 3), content.ViewSize);
    }

    [TestMethod]
    public void AlignPanel_PutsContentAtTheCornersAndStretchesWhereAsked()
    {
        var panel = new AlignPanel(_theme) { ViewSize = new Size(20, 10) };
        Control content = Child(6, 2);
        panel.SetContent(content);

        panel.HorizontalAlignment = StackAlignment.End;
        panel.VerticalAlignment = StackAlignment.End;
        Assert.AreEqual(new Position(14, 8), content.Position);

        panel.HorizontalAlignment = StackAlignment.Start;
        panel.VerticalAlignment = StackAlignment.Stretch;
        Assert.AreEqual(new Position(0, 0), content.Position);
        Assert.AreEqual(new Size(6, 10), content.ViewSize);
    }

    [TestMethod]
    public void AlignPanel_FollowsAResize_AndShrinksContentThatDoesNotFit()
    {
        AlignPanel panel = AlignPanel.Centered(_theme, Child(8, 4), new Size(8, 4));
        panel.ViewSize = new Size(30, 12);
        panel.OnInitializeLayout();
        IControl content = panel.Controls.AsSnapshot()[0];
        Assert.AreEqual(new Position(11, 4), content.Position);

        panel.ViewSize = new Size(6, 2);
        panel.OnInitializeLayout();
        Assert.AreEqual(new Size(6, 2), content.ViewSize);
        Assert.AreEqual(new Position(0, 0), content.Position);

        panel.SetContentSize(new Size(2, 2));
        Assert.AreEqual(new Position(2, 0), content.Position);
    }

    [TestMethod]
    public void ConstrainedPanel_HoldsTheContentBetweenTheLimits()
    {
        var panel = new ConstrainedPanel(_theme) { ViewSize = new Size(100, 30), MinWidth = 40, MaxWidth = 60, MaxHeight = 20 };
        Control content = Child(1, 1);

        panel.SetContent(content);
        Assert.AreEqual(new Size(60, 20), content.ViewSize);
        Assert.AreEqual(new Position(0, 0), content.Position);

        panel.ViewSize = new Size(30, 10);
        panel.OnInitializeLayout();
        Assert.AreEqual(new Size(40, 10), content.ViewSize, "the panel is narrower than the minimum, so the content is cut by its edge");

        panel.ViewSize = new Size(50, 10);
        panel.OnInitializeLayout();
        Assert.AreEqual(new Size(50, 10), content.ViewSize);
    }

    [TestMethod]
    public void ConstrainedPanel_CanCentreContentThatIsCappedAndAMinimumBeatsAMaximum()
    {
        var panel = new ConstrainedPanel(_theme)
        {
            ViewSize = new Size(100, 10),
            MaxWidth = 60,
            HorizontalAlignment = StackAlignment.Center,
        };
        Control content = Child(1, 1);
        panel.SetContent(content);

        Assert.AreEqual(20, content.Position.X);
        Assert.AreEqual(50, ConstrainedPanel.Constrain(100, minimum: 50, maximum: 20));
        Assert.AreEqual(7, ConstrainedPanel.Constrain(7, 0, int.MaxValue));
    }

    [TestMethod]
    public void TheWrappers_DrawTheirChildren_AndSurviveTinySizes()
    {
        Control content = Child(4, 2);
        AlignPanel panel = AlignPanel.Centered(_theme, content, new Size(4, 2));
        panel.ViewSize = new Size(1, 1);
        var screen = new ScreenBuffer(1, 1);
        panel.Draw(screen);
        panel.ViewSize = new Size(0, 0);
        panel.Draw(screen);

        var constrained = new ConstrainedPanel(_theme) { ViewSize = new Size(2, 1), MinWidth = 5 };
        constrained.SetContent(Child(1, 1));
        constrained.Draw(new ScreenBuffer(2, 1));
    }
}
