using System.Reflection;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// A disabled control, and a read-only one, must not change when it is pressed or clicked: what it draws before and after
/// has to be the same. A control that reacts while disabled looks broken, and one that edits while read-only loses data.
/// </summary>
[TestClass]
public class ControlInertnessSweepTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme
        {
            Foreground = Color.White,
            Background = Color.Black,
            SelectedForeground = Color.Black,
            SelectedBackground = Color.Gray,
            SelectedFocusedForeground = Color.Black,
            SelectedFocusedBackground = Color.Cyan,
            DisabledForeground = Color.DarkGray,
            DisabledSelectedForeground = Color.DarkGray,
            DisabledBackground = Color.Black,
        });
        return m.Object;
    }

    private static readonly ConsoleKey[] Keys =
    [
        ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.LeftArrow, ConsoleKey.RightArrow, ConsoleKey.Home, ConsoleKey.End,
        ConsoleKey.PageUp, ConsoleKey.PageDown, ConsoleKey.Enter, ConsoleKey.Spacebar, ConsoleKey.Backspace, ConsoleKey.Delete,
        ConsoleKey.D5, ConsoleKey.A,
    ];

    // The status bars are laid out by the dialog that owns them (ContainerLayoutGuardTest covers that), so a bare one in a sweep is not a state of use.
    private static IEnumerable<Type> ControlTypes() => typeof(Control).Assembly.GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(Control).IsAssignableFrom(t))
        .Where(t => t.Namespace is not null && t.Namespace.StartsWith("iConsole.Controls", StringComparison.Ordinal))
        .Where(t => t.Name is not ("ViewerStatusBar" or "EditorStatusBar"))
        .Where(t => !typeof(Window).IsAssignableFrom(t))
        .Where(t => !t.IsGenericTypeDefinition || t.GetGenericArguments().Length == 1)
        .Select(t => t.IsGenericTypeDefinition ? TryClose(t) : t)
        .OfType<Type>()
        .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static Type? TryClose(Type definition)
    {
        try
        {
            return definition.MakeGenericType(typeof(string));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Control? Build(Type type) => SweepControlFactory.Build(type, Theme());

    private static async Task PressEverything(Control control, int width, int height)
    {
        foreach (ConsoleKey key in Keys)
        {
            await control.HandleKeyEventAsync(new KeyEvent(key == ConsoleKey.A ? 'a' : key == ConsoleKey.D5 ? '5' : '\0', key, false, false, false));
        }

        foreach ((long x, long y) in new[] { (0L, 0L), (width / 2L, height / 2L), (width - 1L, height - 1L) })
        {
            await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, y), LeftClicked = true });
            await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, y), WheelDelta = 1 });
        }
    }

    private static async Task<List<string>> Sweep(Action<Control> restrict, Func<Control, bool> applies)
    {
        List<string> changed = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = Build(type);
                if (control is null || !applies(control))
                {
                    continue;
                }

                control.Position = new Position(0, 0);
                control.ViewSize = new Size(30, 4);
                restrict(control);
                string before = ScreenBuffer.Render(control, 30, 4).ToString();
                await PressEverything(control, 30, 4);
                string after = ScreenBuffer.Render(control, 30, 4).ToString();
                if (before != after)
                {
                    changed.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                changed.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        return changed;
    }

    [TestMethod]
    public async Task ADisabledControl_DrawsTheSame_AfterEveryKeyAndClick()
    {
        List<string> changed = await Sweep(control => control.IsEnabled = false, _ => true);

        Assert.AreEqual(0, changed.Count, string.Join(", ", changed));
    }

    [TestMethod]
    public async Task AReadOnlyControl_DrawsTheSame_AfterEveryKeyAndClick()
    {
        List<string> changed = await Sweep(control => ((IReadOnly)control).IsReadOnly = true, control => control is IReadOnly);

        Assert.AreEqual(0, changed.Count, string.Join(", ", changed));
    }

    /// <summary>Focusable controls whose focus shows as the text cursor or as a highlight that is always drawn: the buffer does not change.</summary>
    private static readonly HashSet<string> FocusShownElsewhere =
    [
        "Calendar", "FilePicker", "LiveTable", "ActionButton", "ListBox`1", "LogView", "PasswordBox", "PopupListView", "ScrollView", "SearchBox", "Table`1", "TextBox", "MarkdownViewerControl",
    ];

    /// <summary>Controls that cannot be disabled by IsEnabled (a button follows its command) or that have nothing of their own to grey.</summary>
    private static readonly HashSet<string> DisabledShownElsewhere =
    [
        "ActionButton", "Button", "CancelButton", "CancelDialogButton", "CloseButton", "ColorPicker", "FilePicker", "LiveTable", "ListBox`1", "LogView", "PopupListView", "ScrollView", "Table`1", "MarkdownViewerControl",
    ];

    /// <summary>Controls sized by what they hold, or containers that draw only their children: they do not fill a rectangle of their own.</summary>
    private static readonly HashSet<string> SizedByContent =
    [
        "ActionButton", "TabPage", "ToolTipHost", "TextViewerControl", "BouncingLabel", "Accordion", "Badge", "ColorMap", "Control", "TextEditorControl", "FormPanel", "KeybindingLabel", "LinkLabel", "RadioGroup`1", "ScrollBar", "StepIndicator", "Tab", "ToastHost",
    ];

    private static readonly Dictionary<string, Action<Control>> WithContent = new()
    {
        ["Chips"] = c => ((Chips)c).Add("rock"),
        ["Breadcrumb"] = c => ((Breadcrumb)c).Segments = ["Home", "Docs", "Notes"],
        ["CheckList"] = c => ((CheckList)c).SetItems(["alpha", "beta"]),
        ["HexViewer"] = c => ((HexViewer)c).Data = [1, 2, 3, 4, 5, 6, 7, 8],
        ["LinkLabel"] = c => ((LinkLabel)c).Text = "a link",
        ["LogView"] = c => ((LogView)c).Append("a log line"),
        ["Pagination"] = c => { ((Pagination)c).TotalItems = 100; ((Pagination)c).Page = 3; },
        ["PopupListView"] = c => ((PopupListView)c).SetItems(["red", "green"]),
        ["RadioGroup`1"] = c => ((RadioGroup<string>)c).SetItems(["one", "two"]),
        ["SegmentedControl"] = c => ((SegmentedControl)c).Options = ["Day", "Week"],
        ["Toolbar"] = c => ((Toolbar)c).Items = [new ToolbarItem("Open"), new ToolbarItem("Save")],
        ["MarkdownViewerControl"] = c => ((iConsole.Controls.Viewers.MarkdownViewerControl)c).Markdown = "# Title\n\ntext",
        ["TextBox"] = c => ((TextBox)c).Text = "abc",
        ["SearchBox"] = c => ((SearchBox)c).Text = "abc",
        ["PasswordBox"] = c => ((PasswordBox)c).Text = "abc",
    };

    [TestMethod]
    public void AFocusableControl_LooksDifferent_WhenItHasFocus()
    {
        List<string> same = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = Build(type);
                if (control is null || !control.CanFocus || FocusShownElsewhere.Contains(type.Name))
                {
                    continue;
                }

                if (WithContent.TryGetValue(type.Name, out Action<Control>? fill))
                {
                    fill(control);
                }

                control.Position = new Position(0, 0);
                control.ViewSize = new Size(30, 4);
                ScreenBuffer unfocused = ScreenBuffer.Render(control, 30, 4);
                control.SetFocus();
                ScreenBuffer focused = ScreenBuffer.Render(control, 30, 4);
                bool textDiffers = unfocused.ToString() != focused.ToString();
                bool colourDiffers = false;
                for (int y = 0; y < 4 && !colourDiffers; y++)
                {
                    for (int x = 0; x < 30 && !colourDiffers; x++)
                    {
                        colourDiffers = unfocused.ForegroundAt(x, y) != focused.ForegroundAt(x, y) || unfocused.BackgroundAt(x, y) != focused.BackgroundAt(x, y);
                    }
                }

                if (!textDiffers && !colourDiffers)
                {
                    same.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                same.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, same.Count, string.Join(", ", same));
    }

    [TestMethod]
    public void ADisabledControl_LooksDifferent_FromAnEnabledOne()
    {
        List<string> same = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? enabled = Build(type);
                Control? disabled = Build(type);
                if (enabled is null || disabled is null || !enabled.CanFocus || DisabledShownElsewhere.Contains(type.Name))
                {
                    continue;
                }

                if (WithContent.TryGetValue(type.Name, out Action<Control>? fill))
                {
                    fill(enabled);
                    fill(disabled);
                }

                foreach (Control control in new[] { enabled, disabled })
                {
                    control.Position = new Position(0, 0);
                    control.ViewSize = new Size(30, 4);
                }

                disabled.IsEnabled = false;
                ScreenBuffer on = ScreenBuffer.Render(enabled, 30, 4);
                ScreenBuffer off = ScreenBuffer.Render(disabled, 30, 4);
                bool differs = on.ToString() != off.ToString();
                for (int y = 0; y < 4 && !differs; y++)
                {
                    for (int x = 0; x < 30 && !differs; x++)
                    {
                        differs = on.ForegroundAt(x, y) != off.ForegroundAt(x, y) || on.BackgroundAt(x, y) != off.BackgroundAt(x, y);
                    }
                }

                if (!differs)
                {
                    same.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                same.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, same.Count, string.Join(", ", same));
    }

    [TestMethod]
    public void EveryControl_PaintsItsWholeRectangle_SoNothingStaleIsLeftBehind()
    {
        List<string> unpainted = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = Build(type);
                if (control is null || SizedByContent.Contains(type.Name))
                {
                    continue;
                }

                if (WithContent.TryGetValue(type.Name, out Action<Control>? fill))
                {
                    fill(control);
                }

                control.Position = new Position(0, 0);
                control.ViewSize = new Size(30, 1);
                ScreenBuffer screen = new(30, 1);
                for (int y = 0; y < 1; y++)
                {
                    screen.Write(0, new Position(0, y), new Size(30, 1), new ConxelString(new string('Ø', 30), Color.White, Color.Black));
                }

                control.Draw(screen);
                for (int y = 0; y < 1; y++)
                {
                    if (screen.Line(y).Contains('Ø'))
                    {
                        unpainted.Add(type.Name);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                unpainted.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, unpainted.Count, string.Join(", ", unpainted));
    }

    [TestMethod]
    public void EveryControl_StaysInsideItsRectangle()
    {
        List<string> spilled = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = Build(type);
                if (control is null)
                {
                    continue;
                }

                if (WithContent.TryGetValue(type.Name, out Action<Control>? fill))
                {
                    fill(control);
                }

                // A 12 x 2 control in the middle of a 40 x 8 screen whose every cell holds a sentinel.
                control.Position = new Position(10, 3);
                control.ViewSize = new Size(12, 2);
                ScreenBuffer screen = new(40, 8);
                for (int y = 0; y < 8; y++)
                {
                    screen.Write(0, new Position(0, y), new Size(40, 1), new ConxelString(new string('Ø', 40), Color.White, Color.Black));
                }

                control.Draw(screen);
                bool spill = false;
                for (int y = 0; y < 8 && !spill; y++)
                {
                    string line = screen.Line(y);
                    for (int x = 0; x < 40 && !spill; x++)
                    {
                        bool inside = x >= 10 && x < 22 && y >= 3 && y < 5;
                        spill = !inside && line[x] != 'Ø';
                    }
                }

                if (spill)
                {
                    spilled.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                spilled.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, spilled.Count, string.Join(", ", spilled));
    }

    [TestMethod]
    public void EveryControl_DrawsTheSameTwiceInARow()
    {
        List<string> unstable = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = Build(type);
                if (control is null || AnimatesItself.Contains(type.Name))
                {
                    continue;
                }

                if (WithContent.TryGetValue(type.Name, out Action<Control>? fill))
                {
                    fill(control);
                }

                control.Position = new Position(0, 0);
                control.ViewSize = new Size(30, 3);
                string first = ScreenBuffer.Render(control, 30, 3).ToString();
                string second = ScreenBuffer.Render(control, 30, 3).ToString();
                if (first != second)
                {
                    unstable.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                unstable.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, unstable.Count, string.Join(", ", unstable));
    }

    /// <summary>Controls whose picture follows the clock.</summary>
    private static readonly HashSet<string> AnimatesItself = ["Spinner", "Skeleton", "Date", "ProgressBar", "Countdown", "ElapsedClock", "ToastHost"];

    [TestMethod]
    public void ADisabledControl_ListsNoKeys_ItWillNotTake()
    {
        List<string> lying = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = Build(type);
                if (control is null || !control.CanFocus || type.Name == "PopupListView")
                {
                    continue;
                }

                if (WithContent.TryGetValue(type.Name, out Action<Control>? fill))
                {
                    fill(control);
                }

                control.IsEnabled = false;
                if (control.GetKeyHints().Any() && control.IsEnabled == false)
                {
                    lying.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                lying.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, lying.Count, string.Join(", ", lying));
    }

    [TestMethod]
    public void TheKeyHints_AreWhole_AndNameEachKeyOnce()
    {
        List<string> faulty = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = Build(type);
                if (control is null)
                {
                    continue;
                }

                if (WithContent.TryGetValue(type.Name, out Action<Control>? fill))
                {
                    fill(control);
                }

                List<(string Key, string Description)> hints = [.. control.GetKeyHints()];
                bool whole = hints.All(h => !string.IsNullOrWhiteSpace(h.Key) && !string.IsNullOrWhiteSpace(h.Description));
                bool once = hints.Select(h => h.Key).Distinct().Count() == hints.Count;
                if (!whole || !once)
                {
                    faulty.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                faulty.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, faulty.Count, string.Join(", ", faulty));
    }
}
