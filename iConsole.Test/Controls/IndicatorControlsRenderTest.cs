using iConsole.Controls;
using iConsole.Controls.Progress;
using ScrollBarControl = iConsole.Controls.ScrollBar.ScrollBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// The rendered read-only indicators and containers — <see cref="ProgressBar"/>,
/// <see cref="Meter"/>, <see cref="ScrollBarControl"/>, <see cref="Spinner"/>,
/// <see cref="KeybindingLabel"/>, <see cref="Separator"/>, <see cref="GroupBox"/> and
/// <see cref="FormPanel"/>.
/// </summary>
[TestClass]
public class IndicatorControlsRenderTest
{
    private readonly TestThemeService _theme = new();
    private ColorScheme Scheme => _theme.CurrentColorScheme;

    #region ProgressBar
    private ProgressBar CreateProgressBar(int current, int width = 30) => new(_theme)
    {
        Current = current,
        ViewSize = new Size(width, 1),
    };

    [TestMethod]
    public void ProgressBar_DrawsLabelBarAndCentredPercentage()
    {
        var screen = ScreenBuffer.Render(CreateProgressBar(50), 30, 1);

        // An 18-cell bar, half full, the percentage over its middle.
        Assert.AreEqual("Progress: [▬▬▬▬▬▬ 50% ‧‧‧‧‧‧‧]", screen.Lines[0]);
    }

    [TestMethod]
    public void ProgressBar_KeepsItsWidthAtEveryValue()
    {
        var progressBar = CreateProgressBar(0);
        for (var current = 0; current <= 100; current += 3)
        {
            progressBar.Current = current;
            var line = ScreenBuffer.Render(progressBar, 30, 1).Line(0);

            // The closing bracket never moves: the bar used to lose a cell to rounding at most
            // values, so it jittered as a task progressed.
            Assert.AreEqual(']', line[29], $"at {current}%: '{line}'");
        }
    }

    [TestMethod]
    public void ProgressBar_FullAndEmpty()
    {
        var full = ScreenBuffer.Render(CreateProgressBar(100), 30, 1).Lines[0];
        var empty = ScreenBuffer.Render(CreateProgressBar(0), 30, 1).Lines[0];

        Assert.IsFalse(full.Contains('‧'));
        Assert.IsTrue(full.Contains("100%"));
        Assert.IsFalse(empty.Contains('▬'));
        Assert.IsTrue(empty.Contains(" 0% "));
    }

    [TestMethod]
    public void ProgressBar_OutOfRange_IsClampedAndStillDrawn()
    {
        Assert.IsTrue(ScreenBuffer.Render(CreateProgressBar(150), 30, 1).Contains("100%"));
        Assert.IsTrue(ScreenBuffer.Render(CreateProgressBar(-20), 30, 1).Contains(" 0% "));
        Assert.AreEqual(1.0, CreateProgressBar(150).Fraction);
        Assert.AreEqual(0.0, CreateProgressBar(-20).Fraction);
    }

    [TestMethod]
    public void ProgressBar_EmptyRange_DrawsZeroInsteadOfFailing()
    {
        var progressBar = CreateProgressBar(5);
        progressBar.Min = 10;
        progressBar.Max = 10;

        var screen = ScreenBuffer.Render(progressBar, 30, 1);

        Assert.AreEqual(0.0, progressBar.Fraction);
        Assert.IsTrue(screen.Contains(" 0% "));
    }

    [TestMethod]
    public void ProgressBar_Label_FollowsTheTextBoxCaptionRules()
    {
        var unlabelled = CreateProgressBar(0, 20);
        unlabelled.Label = string.Empty;
        var colon = CreateProgressBar(0, 20);
        colon.Label = "Copy:";

        Assert.IsTrue(ScreenBuffer.Render(unlabelled, 20, 1).Lines[0].StartsWith("[‧"));
        Assert.IsTrue(ScreenBuffer.Render(colon, 20, 1).Lines[0].StartsWith("Copy: [‧"));
    }

    [TestMethod]
    public void ProgressBar_PaintsItsWholeWidth_EvenWhenTooNarrow()
    {
        var progressBar = CreateProgressBar(50, 8);

        var screen = ScreenBuffer.Render(progressBar, 20, 1);

        screen.AssertFillsBounds(progressBar);
        Assert.IsFalse(screen.IsWritten(8, 0));
    }

    [TestMethod]
    public void ProgressBar_Colours_NormalAndDisabled()
    {
        var disabled = CreateProgressBar(50);
        disabled.IsEnabled = false;

        ScreenBuffer.Render(CreateProgressBar(50), 30, 1).AssertColors(0, 0, 30, Scheme.Foreground, Scheme.Background);
        ScreenBuffer.Render(disabled, 30, 1).AssertColors(0, 0, 30, Scheme.DisabledForeground, Scheme.DisabledBackground);
    }
    #endregion

    #region Meter
    [TestMethod]
    public void Meter_Disabled_UsesTheDisabledColours()
    {
        var meter = new Meter(_theme) { Value = 0.5, ViewSize = new Size(10, 1), IsEnabled = false };

        var screen = ScreenBuffer.Render(meter, 10, 1);

        Assert.AreEqual("█████░░░░░", screen.Lines[0]);
        screen.AssertColors(0, 0, 10, Scheme.DisabledForeground, Scheme.DisabledBackground);
    }
    #endregion

    #region ScrollBar
    private ScrollBarControl CreateScrollBar(int cursor, int contentHeight, int height = 5)
    {
        var model = new ScrollableModel { ScrollableSize = new Size(1, contentHeight) };
        model.CursorPosition.Y = (uint)cursor;
        return new ScrollBarControl(_theme) { ScrollableModel = model, ViewSize = new Size(1, height) };
    }

    [TestMethod]
    public void ScrollBar_Vertical_ThumbFollowsTheCursor()
    {
        ScreenBuffer.Render(CreateScrollBar(0, 11), 1, 5).AssertLines("↑", "⬤", "│", "│", "↓");
        ScreenBuffer.Render(CreateScrollBar(5, 11), 1, 5).AssertLines("↑", "│", "⬤", "│", "↓");
        ScreenBuffer.Render(CreateScrollBar(10, 11), 1, 5).AssertLines("↑", "│", "│", "⬤", "↓");
    }

    [TestMethod]
    public void ScrollBar_Vertical_PaintsItsWholeHeight()
    {
        var scrollBar = CreateScrollBar(3, 11, height: 8);

        ScreenBuffer.Render(scrollBar, 1, 8).AssertFillsBounds(scrollBar);
    }

    [TestMethod]
    public void ScrollBar_Horizontal_ThumbFollowsTheCursor()
    {
        var model = new ScrollableModel { ScrollableSize = new Size(11, 1) };
        model.CursorPosition.X = 10;
        var scrollBar = new ScrollBarControl(_theme)
        {
            ScrollableModel = model,
            ScrollBarOrientation = Orientation.Horizontal,
            ViewSize = new Size(6, 1),
        };

        Assert.AreEqual("←⎯⎯⎯⬤→", ScreenBuffer.Render(scrollBar, 6, 1).Lines[0]);
    }

    [TestMethod]
    public void ScrollBar_TooShortForArrows_DrawsOnlyTheThumb()
    {
        Assert.AreEqual("⬤", ScreenBuffer.Render(CreateScrollBar(0, 10, height: 2), 1, 2).ToString());
    }
    #endregion

    #region Spinner
    [TestMethod]
    public void Spinner_ActiveShowsAFrame_InactiveShowsOnlyTheLabel()
    {
        var spinner = new Spinner(_theme, Mock.Of<IDateTimeProviderService>())
        {
            Label = "Indexing",
            ViewSize = new Size(12, 1),
        };

        Assert.AreEqual("⠋ Indexing", ScreenBuffer.Render(spinner, 12, 1).Lines[0]);

        spinner.IsActive = false;
        Assert.AreEqual("  Indexing", ScreenBuffer.Render(spinner, 12, 1).Lines[0]);
    }

    [TestMethod]
    public void Spinner_ColourOverrides_ReachTheBuffer()
    {
        var spinner = new Spinner(_theme, Mock.Of<IDateTimeProviderService>())
        {
            ViewSize = new Size(3, 1),
            Foreground = Color.Red,
            Background = Color.Black,
        };

        ScreenBuffer.Render(spinner, 3, 1).AssertColors(0, 0, 3, Color.Red, Color.Black);
    }
    #endregion

    #region KeybindingLabel
    [TestMethod]
    public void KeybindingLabel_DrawsTheKeyCapThenTheDescription()
    {
        var label = new KeybindingLabel(_theme)
        {
            Binding = new Key(ConsoleKey.S, false, false, true),
            Label = "Save",
            ViewSize = new Size(16, 1),
        };

        var screen = ScreenBuffer.Render(label, 16, 1);

        Assert.AreEqual("[Ctrl+S] Save", screen.Lines[0]);
        screen.AssertColors("[Ctrl+S]", Scheme.SelectedFocusedForeground, Scheme.Background);
        screen.AssertColors(" Save", Scheme.Foreground, Scheme.Background);
        screen.AssertFillsBounds(label);
    }

    [TestMethod]
    public void KeybindingLabel_ArrowAndNamedKeys_UseShortGlyphs()
    {
        var label = new KeybindingLabel(_theme)
        {
            Binding = new Key(ConsoleKey.UpArrow, true, true, false),
            ViewSize = new Size(16, 1),
        };

        Assert.AreEqual("[Alt+Shift+↑]", ScreenBuffer.Render(label, 16, 1).Lines[0]);
    }
    #endregion

    #region Separator
    [TestMethod]
    public void Separator_HorizontalAndVertical()
    {
        var horizontal = new Separator(_theme) { ViewSize = new Size(5, 1) };
        var vertical = new Separator(_theme) { Orientation = Orientation.Vertical, ViewSize = new Size(1, 3) };

        Assert.AreEqual("⎯⎯⎯⎯⎯", ScreenBuffer.Render(horizontal, 5, 1).Lines[0]);
        ScreenBuffer.Render(vertical, 1, 3).AssertLines("│", "│", "│");
        ScreenBuffer.Render(horizontal, 5, 1).AssertColors(0, 0, 5, Scheme.Foreground, Scheme.Background);
    }
    #endregion

    #region GroupBox
    [TestMethod]
    public void GroupBox_DrawsADoubleBorderWithTheTitleInTheTopEdge()
    {
        var groupBox = new GroupBox(_theme) { Title = "Options", ViewSize = new Size(20, 4) };

        var screen = ScreenBuffer.Render(groupBox, 20, 4);

        screen.AssertLines(
            "╔══ Options ═══════╗",
            "║                  ║",
            "║                  ║",
            "╚══════════════════╝");
        screen.AssertFillsBounds(groupBox);
    }

    [TestMethod]
    public void GroupBox_ClearsItsInterior_AndDrawsItsContentInside()
    {
        var groupBox = new GroupBox(_theme) { Title = "Box", ViewSize = new Size(12, 3) };
        groupBox.SetContent(new Label("inside", _theme));
        var screen = new ScreenBuffer(12, 3);
        screen.Write(0, new Position(1, 1), new Size(10, 1), new ConxelString("garbage!!!", Color.Red, Color.Black));

        groupBox.Draw(screen);

        Assert.AreEqual("║inside    ║", screen.Lines[1]);
    }

    [TestMethod]
    public void GroupBox_TitleTooLong_IsCutBetweenTheCorners()
    {
        var groupBox = new GroupBox(_theme) { Title = "A rather long title", ViewSize = new Size(12, 3) };

        var top = ScreenBuffer.Render(groupBox, 12, 3).Lines[0];

        Assert.AreEqual("╔══ A rathe╗", top);
    }
    #endregion

    #region FormPanel
    private TextBox CreateField(string text) =>
        new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), _theme) { Text = text };

    [TestMethod]
    public void FormPanel_RightAlignsLabels_AndLinesUpTheFields()
    {
        var form = new FormPanel(_theme) { ViewSize = new Size(30, 2) };
        form.AddField("Name", CreateField("readme.md"));
        form.AddField("Directory", CreateField("/tmp"));

        var screen = ScreenBuffer.Render(form, 30, 2);

        Assert.IsTrue(screen.Lines[0].StartsWith("     Name: [readme.md"), screen.ToString());
        Assert.IsTrue(screen.Lines[1].StartsWith("Directory: [/tmp"), screen.ToString());
        // The fields draw no caption of their own: no second colon before the well.
        Assert.IsFalse(screen.Contains(": :"));
    }

    [TestMethod]
    public void FormPanel_HighlightsTheCaptionOfTheFocusedField()
    {
        var form = new FormPanel(_theme) { ViewSize = new Size(30, 2) };
        var name = CreateField("readme.md");
        var directory = CreateField("/tmp");
        form.AddField("Name", name);
        form.AddField("Directory", directory);

        directory.SetFocus();
        var screen = ScreenBuffer.Render(form, 30, 2);

        screen.AssertColors("     Name: ", Scheme.Foreground, Scheme.Background);
        screen.AssertColors("Directory: ", Scheme.SelectedFocusedForeground, Scheme.Background);
    }

    [TestMethod]
    public void FormPanel_RowGap_SpacesTheFields()
    {
        var form = new FormPanel(_theme) { ViewSize = new Size(30, 3), RowGap = 1 };
        form.AddField("A", CreateField("1"));
        form.AddField("B", CreateField("2"));

        var screen = ScreenBuffer.Render(form, 30, 3);

        Assert.IsTrue(screen.Lines[0].StartsWith("A: [1"));
        Assert.AreEqual(string.Empty, screen.Lines[1]);
        Assert.IsTrue(screen.Lines[2].StartsWith("B: [2"));
    }
    #endregion

    [TestMethod]
    public async Task ScrollBar_Disabled_IgnoresTheWheelAndClicks()
    {
        ScrollBarControl bar = CreateScrollBar(2, 11);
        bar.Position = new Position(0, 0);
        bar.IsEnabled = false;
        long before = bar.ScrollableModel!.CursorPosition.Y;

        Assert.IsFalse(await bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 2), WheelDelta = -1 }));
        Assert.IsFalse(await bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 4), LeftClicked = true }));

        Assert.AreEqual(before, bar.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task ScrollBar_Enabled_TheWheelMovesTheCursor()
    {
        ScrollBarControl bar = CreateScrollBar(2, 11);
        bar.Position = new Position(0, 0);

        Assert.IsTrue(await bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 2), WheelDelta = -1 }));

        Assert.IsTrue(bar.ScrollableModel!.CursorPosition.Y > 2);
    }
}
