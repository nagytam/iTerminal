using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Test.Core;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iConsole.Test.Controls;

/// <summary>
/// What <see cref="Label"/> and <see cref="DynamicLabel{T}"/> leave on screen — the characters,
/// their colours and style, and above all that a shorter text erases the longer one before it.
/// </summary>
[TestClass]
public class LabelRenderTest
{
    private readonly TestThemeService _theme = new();

    private Label CreateLabel(string text, int width) => new(text, _theme)
    {
        Position = new Position(2, 1),
        ViewSize = new Size(width, 1),
    };

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_WritesTextAtItsPosition()
    {
        var screen = ScreenBuffer.Render(CreateLabel("Hello", 10), 20, 3);

        screen.AssertLines(
            "",
            "  Hello");
        Assert.AreEqual(new Position(2, 1), screen.Find("Hello"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_UsesThemeColoursByDefault()
    {
        var screen = ScreenBuffer.Render(CreateLabel("Hello", 10), 20, 3);

        var scheme = _theme.CurrentColorScheme;
        screen.AssertColors("Hello", scheme.Foreground, scheme.Background);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_PaintsItsWholeWidth()
    {
        var label = CreateLabel("Hi", 10);
        var screen = ScreenBuffer.Render(label, 20, 3);

        screen.AssertFillsBounds(label);
        // The padding is in the label's background, not the terminal default.
        Assert.AreEqual(_theme.CurrentColorScheme.Background, screen.BackgroundAt(11, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShorterTextErasesThePreviousOne()
    {
        var label = CreateLabel("Loading...", 12);
        var screen = new ScreenBuffer(20, 3);
        label.Draw(screen);

        label.Text = "Done";
        label.Draw(screen);

        Assert.AreEqual("  Done", screen.Lines[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_TextLongerThanTheLabelIsClipped()
    {
        var screen = ScreenBuffer.Render(CreateLabel("A long caption", 6), 20, 3);

        Assert.AreEqual("  A long", screen.Lines[1]);
        Assert.IsFalse(screen.IsWritten(8, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_CustomColoursAndStyleReachTheBuffer()
    {
        var label = CreateLabel("Bold", 4);
        label.ForegroundColor = Color.Red;
        label.BackgroundColor = Color.Black;
        label.IsBold = true;
        label.IsUnderline = true;

        var screen = ScreenBuffer.Render(label, 20, 3);

        var cell = screen.CellOf("Bold");
        Assert.AreEqual(Color.Red, cell.ForegroundColor);
        Assert.AreEqual(Color.Black, cell.BackgroundColor);
        Assert.IsTrue(cell.IsBold);
        Assert.IsTrue(cell.IsUnderline);
        Assert.IsFalse(cell.IsItalic);
        Assert.IsFalse(cell.IsStrikeThrough);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ChildOfAParentIsDrawnAtTheAbsolutePosition()
    {
        var parent = new Control(_theme) { Position = new Position(5, 2), ViewSize = new Size(20, 5) };
        var label = new Label("Child", _theme) { Position = new Position(1, 1), ViewSize = new Size(5, 1) };
        parent.AddControl(label);

        var screen = ScreenBuffer.Render(parent, 30, 6);

        Assert.AreEqual(new Position(6, 3), screen.Find("Child"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_HiddenChildIsNotDrawnByItsParent()
    {
        var parent = new Control(_theme) { ViewSize = new Size(20, 5) };
        var label = new Label("Hidden", _theme) { ViewSize = new Size(6, 1), IsVisible = false };
        parent.AddControl(label);

        var screen = ScreenBuffer.Render(parent, 20, 5);

        Assert.IsFalse(screen.Contains("Hidden"));
    }

    private sealed class Status : INotifyPropertyChanged
    {
        private string _text = string.Empty;

        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabel_DrawsTheCurrentValue()
    {
        var status = new Status { Text = "Idle" };
        var label = new DynamicLabel<Status>(new ObservableValue<Status>(status, s => s.Text), _theme)
        {
            ViewSize = new Size(15, 1),
        };
        var screen = new ScreenBuffer(20, 1);

        label.Draw(screen);
        Assert.AreEqual("Idle", screen.Lines[0]);

        status.Text = "Copying 3 files";
        label.Draw(screen);
        Assert.AreEqual("Copying 3 files", screen.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabel_ShorterValueErasesThePreviousOne()
    {
        var status = new Status { Text = "Copying 3 files" };
        var label = new DynamicLabel<Status>(new ObservableValue<Status>(status, s => s.Text), _theme)
        {
            ViewSize = new Size(15, 1),
        };
        var screen = new ScreenBuffer(20, 1);
        label.Draw(screen);

        status.Text = "Done";
        label.Draw(screen);

        Assert.AreEqual("Done", screen.Lines[0]);
        screen.AssertFillsBounds(label);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabel_DisabledUsesDisabledColours()
    {
        var status = new Status { Text = "Off" };
        var label = new DynamicLabel<Status>(new ObservableValue<Status>(status, s => s.Text), _theme)
        {
            ViewSize = new Size(5, 1),
            IsEnabled = false,
        };

        var screen = ScreenBuffer.Render(label, 10, 1);

        var scheme = _theme.CurrentColorScheme;
        screen.AssertColors(0, 0, 5, scheme.DisabledForeground, scheme.DisabledBackground);
    }
}
