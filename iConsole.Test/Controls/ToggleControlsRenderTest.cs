using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>
/// The rendered states of the controls a user toggles or presses — <see cref="CheckBox"/>,
/// <see cref="RadioButton"/>, <see cref="ToggleButton"/> and <see cref="Button"/>. Each state
/// (normal, focused, disabled, read-only, on/off) must be tellable apart on screen, which in a
/// terminal means by glyph or by colour.
/// </summary>
[TestClass]
public class ToggleControlsRenderTest
{
    private readonly TestThemeService _theme = new();
    private ColorScheme Scheme => _theme.CurrentColorScheme;

    #region CheckBox
    private CheckBox CreateCheckBox(string label, bool isChecked = false) => new(_theme)
    {
        Label = label,
        IsChecked = isChecked,
        ViewSize = new Size(16, 1),
    };

    [Timeout(60_000)]
    [TestMethod]
    public void CheckBox_HotKey_IsPickedOutInTheLabel()
    {
        var checkBox = CreateCheckBox("Include subfolders");
        checkBox.HotKeyChar = 'S';

        var screen = ScreenBuffer.Render(checkBox, 20, 1);

        // "□ Include subfolders": the 's' at column 10 is recoloured, its neighbours are not.
        Assert.AreEqual('s', screen.CharAt(10, 0));
        Assert.AreEqual(Scheme.SelectedFocusedForeground, screen.ForegroundAt(10, 0));
        Assert.AreEqual(Scheme.Foreground, screen.ForegroundAt(9, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CheckBox_UncheckedAndChecked_UseDistinctGlyphs()
    {
        Assert.AreEqual("□ Hidden files", ScreenBuffer.Render(CreateCheckBox("Hidden files"), 20, 1).Lines[0]);
        Assert.AreEqual("▣ Hidden files", ScreenBuffer.Render(CreateCheckBox("Hidden files", true), 20, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CheckBox_Normal_UsesForegroundOnBackground()
    {
        var screen = ScreenBuffer.Render(CreateCheckBox("Recurse"), 20, 1);

        screen.AssertColors(0, 0, 16, Scheme.Foreground, Scheme.Background);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CheckBox_Focused_UsesFocusForeground()
    {
        var checkBox = CreateCheckBox("Recurse");
        checkBox.SetFocus();

        var screen = ScreenBuffer.Render(checkBox, 20, 1);

        screen.AssertColors("□ Recurse", Scheme.SelectedFocusedForeground, Scheme.Background);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CheckBox_Disabled_IsGreyedAndStruckThrough()
    {
        var checkBox = CreateCheckBox("Recurse");
        checkBox.IsEnabled = false;

        var screen = ScreenBuffer.Render(checkBox, 20, 1);

        screen.AssertColors("□ Recurse", Scheme.DisabledForeground, Scheme.DisabledBackground);
        Assert.IsTrue(screen.CellOf("Recurse").IsStrikeThrough);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CheckBox_ReadOnly_IsGreyedButNotStruckThrough()
    {
        var checkBox = CreateCheckBox("Recurse", true);
        checkBox.IsReadOnly = true;
        checkBox.SetFocus();

        var screen = ScreenBuffer.Render(checkBox, 20, 1);

        // Read-only must not look live even with the cursor on it: Space does nothing here.
        screen.AssertColors("▣ Recurse", Scheme.DisabledForeground, Scheme.DisabledBackground);
        Assert.IsFalse(screen.CellOf("Recurse").IsStrikeThrough);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CheckBox_FillsItsWidth_SoAShorterLabelErasesTheLongerOne()
    {
        var checkBox = CreateCheckBox("Long caption here");
        var screen = new ScreenBuffer(20, 1);
        checkBox.Draw(screen);

        checkBox.Label = "Short";
        checkBox.Draw(screen);

        Assert.AreEqual("□ Short", screen.Lines[0]);
        screen.AssertFillsBounds(checkBox);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CheckBox_Space_RedrawsWithTheCheckedGlyph()
    {
        var checkBox = CreateCheckBox("Recurse");
        checkBox.SetFocus();

        await checkBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.AreEqual("▣ Recurse", ScreenBuffer.Render(checkBox, 20, 1).Lines[0]);
    }
    #endregion

    #region RadioButton
    private RadioButton CreateRadioButton(string label, bool isSelected = false) => new(_theme)
    {
        Label = label,
        IsSelected = isSelected,
        ViewSize = new Size(12, 1),
    };

    [Timeout(60_000)]
    [TestMethod]
    public void RadioButton_SelectedAndUnselected_UseDistinctGlyphs()
    {
        Assert.AreEqual("○ Name", ScreenBuffer.Render(CreateRadioButton("Name"), 12, 1).Lines[0]);
        Assert.AreEqual("◉ Name", ScreenBuffer.Render(CreateRadioButton("Name", true), 12, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RadioButton_FocusedOrHighlighted_UsesFocusForeground()
    {
        var focused = CreateRadioButton("Name");
        focused.SetFocus();
        var highlighted = CreateRadioButton("Size");
        highlighted.IsHighlighted = true;

        ScreenBuffer.Render(focused, 12, 1).AssertColors("○ Name", Scheme.SelectedFocusedForeground, Scheme.Background);
        ScreenBuffer.Render(highlighted, 12, 1).AssertColors("○ Size", Scheme.SelectedFocusedForeground, Scheme.Background);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RadioButton_ReadOnly_UsesDisabledColours()
    {
        var radio = CreateRadioButton("Name", true);
        radio.IsReadOnly = true;

        var screen = ScreenBuffer.Render(radio, 12, 1);

        screen.AssertColors("◉ Name", Scheme.DisabledForeground, Scheme.DisabledBackground);
        Assert.IsFalse(screen.CellOf("Name").IsStrikeThrough);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RadioButton_Disabled_IsStruckThrough()
    {
        var radio = CreateRadioButton("Name");
        radio.IsEnabled = false;

        Assert.IsTrue(ScreenBuffer.Render(radio, 12, 1).CellOf("Name").IsStrikeThrough);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RadioButton_PaintsItsWholeWidth()
    {
        var radio = CreateRadioButton("A");

        ScreenBuffer.Render(radio, 20, 1).AssertFillsBounds(radio);
    }
    #endregion

    #region ToggleButton
    private ToggleButton CreateToggle(bool isOn) => new(_theme)
    {
        IsOn = isOn,
        ViewSize = new Size(7, 1),
    };

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleButton_CentresItsStateInBrackets()
    {
        Assert.AreEqual("[ ON  ]", ScreenBuffer.Render(CreateToggle(true), 7, 1).Lines[0]);
        Assert.AreEqual("[ OFF ]", ScreenBuffer.Render(CreateToggle(false), 7, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleButton_CustomLabels()
    {
        var toggle = CreateToggle(true);
        toggle.LabelOn = "YES";
        toggle.LabelOff = "NO";

        Assert.AreEqual("[ YES ]", ScreenBuffer.Render(toggle, 7, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleButton_OnAndOff_HaveDifferentBackgrounds()
    {
        ScreenBuffer.Render(CreateToggle(true), 7, 1).AssertColors(0, 0, 7, Scheme.SelectedForeground, Scheme.SelectedBackground);
        ScreenBuffer.Render(CreateToggle(false), 7, 1).AssertColors(0, 0, 7, Scheme.Foreground, Scheme.ButtonBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleButton_Focused_KeepsTheOnOffBackground()
    {
        var on = CreateToggle(true);
        on.SetFocus();
        var off = CreateToggle(false);
        off.SetFocus();

        // Focus recolours the text; the background still says which state the toggle is in.
        ScreenBuffer.Render(on, 7, 1).AssertColors(0, 0, 7, Scheme.SelectedFocusedForeground, Scheme.SelectedBackground);
        ScreenBuffer.Render(off, 7, 1).AssertColors(0, 0, 7, Scheme.SelectedFocusedForeground, Scheme.ButtonBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleButton_DisabledAndReadOnly_UseDisabledColours()
    {
        var disabled = CreateToggle(true);
        disabled.IsEnabled = false;
        var readOnly = CreateToggle(true);
        readOnly.IsReadOnly = true;

        ScreenBuffer.Render(disabled, 7, 1).AssertColors(0, 0, 7, Scheme.DisabledForeground, Scheme.DisabledButtonBackground);
        ScreenBuffer.Render(readOnly, 7, 1).AssertColors(0, 0, 7, Scheme.DisabledForeground, Scheme.DisabledButtonBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ToggleButton_Enter_RedrawsTheNewState()
    {
        var toggle = CreateToggle(false);
        toggle.SetFocus();

        await toggle.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        Assert.AreEqual("[ ON  ]", ScreenBuffer.Render(toggle, 7, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleButton_LabelWiderThanTheButton_IsCutInsideTheBrackets()
    {
        var toggle = CreateToggle(true);
        toggle.LabelOn = "ENABLED";
        toggle.ViewSize = new Size(6, 1);

        Assert.AreEqual("[ENAB]", ScreenBuffer.Render(toggle, 10, 1).Lines[0]);
    }
    #endregion

    #region Button
    private sealed class SaveCommand(bool canExecute = true) : Command
    {
        public override string Title => "Save";
        public override Key? Key { get; init; } = new Key(ConsoleKey.F2, false, false, false);
        public override bool CanExecute() => canExecute;
        public override Task ExecuteAsync(object? args) => Task.CompletedTask;
    }

    private Button CreateButton(bool canExecute = true, int width = 10) => new(new SaveCommand(canExecute), _theme)
    {
        ViewSize = new Size(width, 1),
    };

    [Timeout(60_000)]
    [TestMethod]
    public void Button_CentresItsTitleInBrackets()
    {
        Assert.AreEqual("[  Save  ]", ScreenBuffer.Render(CreateButton(), 10, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Button_Normal_Focused_Disabled_AreDistinct()
    {
        var focused = CreateButton();
        focused.SetFocus();

        ScreenBuffer.Render(CreateButton(), 10, 1).AssertColors(0, 0, 10, Scheme.Foreground, Scheme.ButtonBackground);
        ScreenBuffer.Render(focused, 10, 1).AssertColors(0, 0, 10, Scheme.SelectedFocusedForeground, Scheme.ButtonBackground);
        ScreenBuffer.Render(CreateButton(canExecute: false), 10, 1).AssertColors(0, 0, 10, Scheme.DisabledForeground, Scheme.DisabledButtonBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Button_Destructive_IsDrawnOnTheErrorColour()
    {
        var button = CreateButton();
        button.IsDestructive = true;

        ScreenBuffer.Render(button, 10, 1).AssertColors(0, 0, 10, Color.White, Scheme.Error);

        var disabled = CreateButton(canExecute: false);
        disabled.IsDestructive = true;
        ScreenBuffer.Render(disabled, 10, 1).AssertColors(0, 0, 10, Scheme.DisabledForeground, Scheme.DisabledButtonBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Button_HotKey_IsPickedOutInTheTitle()
    {
        var button = CreateButton();
        button.HotKeyChar = 'v';

        var screen = ScreenBuffer.Render(button, 10, 1);

        // "[  Save  ]": the 'v' at column 5 is recoloured, its neighbours are not.
        Assert.AreEqual('v', screen.CharAt(5, 0));
        Assert.AreEqual(Scheme.SelectedFocusedForeground, screen.ForegroundAt(5, 0));
        Assert.AreEqual(Scheme.Foreground, screen.ForegroundAt(4, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Button_TitleWiderThanTheButton_IsCut()
    {
        Assert.AreEqual("[Sa]", ScreenBuffer.Render(CreateButton(width: 4), 10, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Button_TooNarrowForAnyTitle_DrawsOnlyItsBrackets()
    {
        Assert.AreEqual("[]", ScreenBuffer.Render(CreateButton(width: 2), 10, 1).Lines[0]);
    }
    #endregion
}
