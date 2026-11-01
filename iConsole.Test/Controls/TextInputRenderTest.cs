using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Extensions;
using iConsole.Model.Validations;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// The rendered single-line inputs — <see cref="TextBox"/>, <see cref="PasswordBox"/>,
/// <see cref="SearchBox"/>, <see cref="ComboBox{T}"/> and <see cref="NumericUpDown"/>. They share
/// one visual language: an optional caption, then a bracketed "well" in the field colours; the
/// caption carries focus and the well carries read-only / invalid.
/// </summary>
[TestClass]
public class TextInputRenderTest
{
    private static readonly Color WellForeground = Color.Green;
    private static readonly Color WellBackground = Color.Yellow;
    private static readonly Color ReadOnlyWellBackground = Color.DarkYellow;

    private readonly TestThemeService _theme = new();
    private ColorScheme Scheme => _theme.CurrentColorScheme;

    private TextBox CreateTextBox(string label, string text = "", int width = 20) =>
        new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), _theme)
        {
            Label = label,
            Text = text,
            ViewSize = new Size(width, 1),
        };

    private static Task TypeAsync(TextBox textBox, string text) =>
        text.Aggregate(Task.CompletedTask, async (previous, c) =>
        {
            await previous;
            await textBox.HandleKeyEventAsync(new KeyEvent(c, ConsoleKey.A, false, false, false));
        });

    #region TextBox — caption
    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_Label_IsFollowedByAColonAndTheWell()
    {
        var screen = ScreenBuffer.Render(CreateTextBox("Name", "readme.md"), 20, 1);

        Assert.AreEqual("Name: [readme.md  ]", screen.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_LabelEndingInAColon_KeepsItsOwnColon()
    {
        var screen = ScreenBuffer.Render(CreateTextBox("Path or URL: ", "/tmp", 24), 24, 1);

        Assert.AreEqual("Path or URL: [/tmp    ]", screen.Lines[0]);
        Assert.IsFalse(screen.Contains(": :"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_WithoutLabel_StartsWithTheWell()
    {
        var screen = ScreenBuffer.Render(CreateTextBox(string.Empty, "commit message", 20), 20, 1);

        Assert.AreEqual("[commit message   ]", screen.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_FormatLabelPrefix()
    {
        Assert.AreEqual(string.Empty, TextBox.FormatLabelPrefix(null));
        Assert.AreEqual(string.Empty, TextBox.FormatLabelPrefix("   "));
        Assert.AreEqual("Name: ", TextBox.FormatLabelPrefix("Name"));
        Assert.AreEqual("Name: ", TextBox.FormatLabelPrefix("Name:"));
        Assert.AreEqual("Name: ", TextBox.FormatLabelPrefix("Name:  "));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_EditableWidth_IsTheWellInterior()
    {
        var textBox = CreateTextBox("Name", width: 20);
        var screen = ScreenBuffer.Render(textBox, 20, 1);

        var open = screen.Line(0).IndexOf('[');
        var close = screen.Line(0).IndexOf(']');
        Assert.AreEqual(textBox.EditableWidth, close - open - 1);
    }
    #endregion

    #region TextBox — colours
    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_Normal_CaptionInForeground_WellInFieldColours()
    {
        var screen = ScreenBuffer.Render(CreateTextBox("Name", "x"), 20, 1);

        screen.AssertColors("Name: ", Scheme.Foreground, Scheme.Background);
        screen.AssertColors(6, 0, 13, WellForeground, WellBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_Focused_HighlightsTheCaption()
    {
        var textBox = CreateTextBox("Name", "x");
        textBox.SetFocus();

        var screen = ScreenBuffer.Render(textBox, 20, 1);

        screen.AssertColors("Name: ", Scheme.SelectedFocusedForeground, Scheme.Background);
        screen.AssertColors(6, 0, 13, WellForeground, WellBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_ReadOnly_DimsTheCaptionAndTheWell()
    {
        var textBox = CreateTextBox("Name", "x");
        textBox.IsReadOnly = true;

        var screen = ScreenBuffer.Render(textBox, 20, 1);

        screen.AssertColors("Name: ", Scheme.DisabledForeground, Scheme.DisabledBackground);
        screen.AssertColors(6, 0, 13, WellForeground, ReadOnlyWellBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_Invalid_DrawsTheWellInTheErrorColour()
    {
        var textBox = CreateTextBox("Age", "abc");
        textBox.ValidationModel = Mock.Of<IValidationModel>(v => v.IsValid == false && v.ErrorMessage == "Not a number");

        var screen = ScreenBuffer.Render(textBox, 20, 1);

        screen.AssertColors("[abc", WellForeground, Scheme.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_HotKey_IsPickedOutInTheCaption()
    {
        var textBox = CreateTextBox("Name");
        textBox.HotKeyChar = 'm';

        var screen = ScreenBuffer.Render(textBox, 20, 1);

        Assert.AreEqual(Scheme.SelectedFocusedForeground, screen.ForegroundAt(2, 0));
        Assert.AreEqual(Scheme.Foreground, screen.ForegroundAt(1, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_PaintsItsWholeWidth_InTheCaptionBackground()
    {
        var textBox = CreateTextBox("Name");
        var screen = ScreenBuffer.Render(textBox, 30, 1);

        screen.AssertFillsBounds(textBox);
        Assert.AreEqual(Scheme.Background, screen.BackgroundAt(19, 0));
    }
    #endregion

    #region TextBox — cursor and scrolling
    [Timeout(60_000)]
    [TestMethod]
    public async Task TextBox_Cursor_SitsAfterTheText()
    {
        var textBox = CreateTextBox("Name", "abc");
        await textBox.OnInitializeAsync();
        textBox.SetFocus();

        ScreenBuffer.Render(textBox, 20, 1);

        Assert.IsTrue(textBox.IsCursorVisible);
        // "Name: [" is seven columns; the cursor is after "abc".
        Assert.AreEqual(new Position(10, 0), textBox.CursorPosition);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextBox_Typing_ShowsWhatWasTyped()
    {
        var textBox = CreateTextBox("Name");
        await textBox.OnInitializeAsync();
        textBox.SetFocus();

        await TypeAsync(textBox, "hello");

        Assert.AreEqual("Name: [hello      ]", ScreenBuffer.Render(textBox, 20, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextBox_TypingPastTheWell_ScrollsAndShowsTheLatestCharacters()
    {
        var textBox = CreateTextBox("Name");
        await textBox.OnInitializeAsync();
        textBox.SetFocus();

        await TypeAsync(textBox, "abcdefghijklmno");
        var screen = ScreenBuffer.Render(textBox, 20, 1);

        // Scrolled: the left edge shows the value continues, the newest characters are visible,
        // and the cursor is still on screen.
        Assert.AreEqual("Name: <efghijklmno]", screen.Lines[0]);
        Assert.IsTrue(textBox.CursorPosition.X < 20);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextBox_ValueLongerThanTheWell_ShowsTheOpenRightEdge()
    {
        var textBox = CreateTextBox("Name", "abcdefghijklmnopqrstuvwxyz");
        await textBox.OnInitializeAsync();
        textBox.ScrollableModel.CursorPosition.X = 0;
        textBox.ScrollableModel.ScrollPosition.X = 0;

        var screen = ScreenBuffer.Render(textBox, 20, 1);

        Assert.AreEqual("Name: [abcdefghijk>", screen.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextBox_Completion_IsGreyedAfterTheTypedText()
    {
        var textBox = CreateTextBox("Country").WithAutoCompletion(["Germany", "Hungary"]);
        await textBox.OnInitializeAsync();
        textBox.SetFocus();

        await TypeAsync(textBox, "Hu");
        await textBox.AutoCompletion!.CompletionModel.UpdateTask!;
        var screen = ScreenBuffer.Render(textBox, 20, 1);

        Assert.AreEqual("Country: [Hungary ]", screen.Lines[0]);
        screen.AssertColors("Hu", WellForeground, WellBackground);
        screen.AssertColors("ngary", Color.Gray, WellBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextBox_LongValueWithACompletion_KeepsTheClosingBracket()
    {
        var textBox = CreateTextBox("Country", width: 16).WithAutoCompletion(["abcdefghijklmnop"]);
        textBox.Text = "abcdefghij";
        await textBox.OnInitializeAsync();
        textBox.ScrollableModel.ScrollPosition.X = 0;

        var screen = ScreenBuffer.Render(textBox, 30, 1);

        // Six columns of well: the value fills it, and nothing spills past the control.
        Assert.AreEqual("Country: [abcd>", screen.Lines[0]);
        Assert.IsFalse(screen.IsWritten(16, 0));
    }
    #endregion

    #region PasswordBox
    private PasswordBox CreatePasswordBox(string text) =>
        new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), _theme)
        {
            Label = "Password",
            Text = text,
            ViewSize = new Size(24, 1),
        };

    [Timeout(60_000)]
    [TestMethod]
    public void PasswordBox_MasksEveryCharacter_AndNeverDrawsTheValue()
    {
        var screen = ScreenBuffer.Render(CreatePasswordBox("hunter2"), 24, 1);

        Assert.AreEqual("Password: [●●●●●●●    ]", screen.Lines[0]);
        Assert.IsFalse(screen.Contains("hunter2"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PasswordBox_NeverDrawsACompletionInClear()
    {
        // The same set-up on a plain text box shows the suggestion — which is the point: on a
        // password box it would be the password, in clear, next to its mask.
        var textBox = CreateTextBox("Password", width: 30).WithAutoCompletion(["secret"]);
        await textBox.OnInitializeAsync();
        Assert.IsTrue(ScreenBuffer.Render(textBox, 30, 1).Contains("secret"));

        var passwordBox = CreatePasswordBox(string.Empty);
        passwordBox.WithAutoCompletion(["secret"]);
        await passwordBox.OnInitializeAsync();

        var screen = ScreenBuffer.Render(passwordBox, 24, 1);

        Assert.IsFalse(screen.Contains("secret"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PasswordBox_CursorFollowsTheMask()
    {
        var passwordBox = CreatePasswordBox("abc");
        await passwordBox.OnInitializeAsync();
        passwordBox.SetFocus();

        ScreenBuffer.Render(passwordBox, 24, 1);

        Assert.AreEqual(new Position(14, 0), passwordBox.CursorPosition);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PasswordBox_UsesTheTextBoxCaptionAndWellColours()
    {
        var passwordBox = CreatePasswordBox("abc");
        passwordBox.SetFocus();

        var screen = ScreenBuffer.Render(passwordBox, 24, 1);

        screen.AssertColors("Password: ", Scheme.SelectedFocusedForeground, Scheme.Background);
        screen.AssertColors("[●●●", WellForeground, WellBackground);
    }
    #endregion

    #region SearchBox
    private SearchBox CreateSearchBox(string text = "") =>
        new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), _theme)
        {
            Text = text,
            ViewSize = new Size(20, 1),
        };

    [Timeout(60_000)]
    [TestMethod]
    public void SearchBox_DrawsItsPrefixWithoutAColon()
    {
        var screen = ScreenBuffer.Render(CreateSearchBox("todo"), 20, 1);

        Assert.AreEqual("/ [todo           ]", screen.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SearchBox_PrefixIsAlwaysInTheAccentColour()
    {
        var screen = ScreenBuffer.Render(CreateSearchBox("todo"), 20, 1);

        screen.AssertColors(0, 0, 1, Scheme.SelectedFocusedForeground, Scheme.Background);
        screen.AssertColors("[todo", WellForeground, WellBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SearchBox_CustomPrefix()
    {
        var searchBox = CreateSearchBox("x");
        searchBox.Prefix = "Find";

        Assert.AreEqual("Find [x           ]", ScreenBuffer.Render(searchBox, 20, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchBox_Escape_ClearsTheWell()
    {
        var searchBox = CreateSearchBox("todo");
        await searchBox.OnInitializeAsync();
        searchBox.SetFocus();

        await searchBox.HandleKeyEventAsync(new KeyEvent('\u001b', ConsoleKey.Escape, false, false, false));

        Assert.AreEqual("/ [               ]", ScreenBuffer.Render(searchBox, 20, 1).Lines[0]);
    }
    #endregion

    #region ComboBox
    private ComboBox<string> CreateComboBox(string label = "Sort")
    {
        var comboBox = new ComboBox<string>(_theme) { Label = label, ViewSize = new Size(20, 1) };
        comboBox.SetItems(["Name", "Size", "Date modified"]);
        return comboBox;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ComboBox_ShowsTheSelectionInAWellWithADropDownGlyph()
    {
        var screen = ScreenBuffer.Render(CreateComboBox(), 20, 1);

        Assert.AreEqual("Sort: [Name       ]▾", screen.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ComboBox_LongSelection_IsCutToTheWell()
    {
        var comboBox = CreateComboBox("Order by");
        comboBox.SelectedIndex = 2;

        Assert.AreEqual("Order by: [Date mo]▾", ScreenBuffer.Render(comboBox, 20, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ComboBox_Colours_ForNormalFocusedAndReadOnly()
    {
        var normal = ScreenBuffer.Render(CreateComboBox(), 20, 1);
        normal.AssertColors("Sort: ", Scheme.Foreground, Scheme.Background);
        normal.AssertColors("[Name", WellForeground, WellBackground);
        normal.AssertColors("▾", Scheme.Foreground, Scheme.Background);

        var focusedComboBox = CreateComboBox();
        focusedComboBox.SetFocus();
        var focused = ScreenBuffer.Render(focusedComboBox, 20, 1);
        focused.AssertColors("Sort: ", Scheme.SelectedFocusedForeground, Scheme.Background);
        focused.AssertColors("▾", Scheme.SelectedFocusedForeground, Scheme.Background);

        var readOnlyComboBox = CreateComboBox();
        readOnlyComboBox.IsReadOnly = true;
        var readOnly = ScreenBuffer.Render(readOnlyComboBox, 20, 1);
        readOnly.AssertColors("Sort: ", Scheme.DisabledForeground, Scheme.DisabledBackground);
        readOnly.AssertColors("[Name", WellForeground, ReadOnlyWellBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComboBox_DownArrow_RedrawsTheNextItem()
    {
        var comboBox = CreateComboBox();
        comboBox.SetFocus();

        await comboBox.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));

        Assert.AreEqual("Sort: [Size       ]▾", ScreenBuffer.Render(comboBox, 20, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ComboBox_Empty_DrawsAnEmptyWell()
    {
        var comboBox = new ComboBox<string>(_theme) { ViewSize = new Size(10, 1) };

        Assert.AreEqual("[       ]▾", ScreenBuffer.Render(comboBox, 10, 1).Lines[0]);
    }
    #endregion

    #region NumericUpDown
    private NumericUpDown CreateNumeric(string label = "", long value = 42) => new(_theme)
    {
        Label = label,
        Minimum = 0,
        Maximum = 100,
        Value = value,
        ViewSize = new Size(16, 1),
    };

    [Timeout(60_000)]
    [TestMethod]
    public void NumericUpDown_RightAlignsTheValueToTheWidestBound()
    {
        Assert.AreEqual("[ 42]↑↓", ScreenBuffer.Render(CreateNumeric(), 16, 1).Lines[0]);
        Assert.AreEqual("[  7]↑↓", ScreenBuffer.Render(CreateNumeric(value: 7), 16, 1).Lines[0]);
        Assert.AreEqual("Count: [100]↑↓", ScreenBuffer.Render(CreateNumeric("Count", 100), 16, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NumericUpDown_Arrows_TakeTheFocusColourWhenFocused()
    {
        var focused = CreateNumeric();
        focused.SetFocus();

        ScreenBuffer.Render(CreateNumeric(), 16, 1).AssertColors("↑↓", Scheme.Foreground, Scheme.Background);
        ScreenBuffer.Render(focused, 16, 1).AssertColors("↑↓", Scheme.SelectedFocusedForeground, Scheme.Background);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NumericUpDown_ReadOnly_DimsTheArrowsAndTheWell()
    {
        var numeric = CreateNumeric();
        numeric.IsReadOnly = true;

        var screen = ScreenBuffer.Render(numeric, 16, 1);

        screen.AssertColors("↑↓", Scheme.DisabledForeground, Scheme.DisabledBackground);
        screen.AssertColors("[ 42]", WellForeground, ReadOnlyWellBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NumericUpDown_UpArrow_RedrawsTheIncrementedValue()
    {
        var numeric = CreateNumeric();
        numeric.SetFocus();

        await numeric.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.UpArrow, false, false, false));

        Assert.AreEqual("[ 43]↑↓", ScreenBuffer.Render(numeric, 16, 1).Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NumericUpDown_PaintsItsWholeWidth()
    {
        var numeric = CreateNumeric();

        ScreenBuffer.Render(numeric, 20, 1).AssertFillsBounds(numeric);
    }
    #endregion
}
