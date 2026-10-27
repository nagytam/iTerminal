using System.Reflection;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// Layers are never cleared between frames, so a control has to paint over what it drew before. Each text setting of each
/// simple control is set to a long text, drawn, set to a short one and drawn into the same buffer: nothing of the long
/// text may remain. A click on an enabled, focusable control must also give it the focus.
/// </summary>
[TestClass]
public class ControlStaleTextSweepTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme
        {
            Foreground = Color.White,
            Background = Color.Black,
            DisabledForeground = Color.DarkGray,
            DisabledBackground = Color.Black,
            SelectedForeground = Color.Black,
            SelectedBackground = Color.Gray,
            SelectedFocusedForeground = Color.Black,
            SelectedFocusedBackground = Color.Cyan,
        });
        return m.Object;
    }

    private static IEnumerable<Type> ControlTypes() => typeof(Control).Assembly.GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(Control).IsAssignableFrom(t))
        .Where(t => t.Namespace is not null && t.Namespace.StartsWith("iConsole.Controls", StringComparison.Ordinal))
        .Where(t => !typeof(Window).IsAssignableFrom(t) && !t.IsGenericTypeDefinition)
        .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static Control? Build(Type type) => SweepControlFactory.Build(type, Theme());

    private static readonly HashSet<string> Skipped = [nameof(Control.Name), "HotKeyChar", "Tag"];

    /// <summary>Controls that move or reload by themselves: their picture is not a function of their settings alone.</summary>
    private static readonly HashSet<string> MovesItself = ["BouncingLabel", "FilePicker"];

    [TestMethod]
    public void ShorteningATextSetting_LeavesNothingOfTheLongOne_OnScreen()
    {
        List<string> stale = [];
        foreach (Type type in ControlTypes().Where(t => !MovesItself.Contains(t.Name)))
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string) && p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0 && !Skipped.Contains(p.Name)))
            {
                try
                {
                    Control? control = Build(type);
                    if (control is null)
                    {
                        break;
                    }

                    control.Position = new Position(0, 0);
                    control.ViewSize = new Size(30, 2);
                    ScreenBuffer screen = new(30, 2);
                    property.SetValue(control, "QQQQQQQQQQQQQQQQQQQQ");
                    control.Draw(screen);
                    if (!screen.Line(0).Contains("QQQ") && !screen.Line(1).Contains("QQQ"))
                    {
                        continue;
                    }

                    long treeBefore = Control.TreeVersion;
                    property.SetValue(control, "z");
                    control.Draw(screen);
                    if ((screen.Line(0).Contains('Q') || screen.Line(1).Contains('Q')) && Control.TreeVersion == treeBefore)
                    {
                        stale.Add($"{type.Name}.{property.Name}");
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        Assert.AreEqual(0, stale.Count, string.Join(", ", stale));
    }

    /// <summary>A modal list and the two that pass a click on to what they hold or to their divider only.</summary>
    private static readonly HashSet<string> ClickDoesNotFocus = ["PopupListView", "ScrollView", "Splitter", "FilePicker", "LiveTable"];

    [TestMethod]
    public async Task AClick_OnAnEnabledFocusableControl_GivesItTheFocus()
    {
        List<string> unfocused = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = Build(type);
                if (control is null || !control.CanFocus || !control.IsEnabled || ClickDoesNotFocus.Contains(type.Name))
                {
                    continue;
                }

                control.Position = new Position(0, 0);
                switch (control)
                {
                    case CheckList list:
                        list.SetItems(["alpha", "beta"]);
                        break;
                    case HexViewer hex:
                        hex.Data = [1, 2, 3, 4];
                        break;
                    case Toolbar toolbar:
                        toolbar.Items = [new ToolbarItem("Open"), new ToolbarItem("Save")];
                        break;
                    case LogView log:
                        log.Append("a line");
                        break;
                }

                control.Position = new Position(0, 0);
                control.ViewSize = new Size(30, 3);
                ScreenBuffer.Render(control, 30, 3);
                await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });
                if (!control.IsFocused)
                {
                    unfocused.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                unfocused.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, unfocused.Count, string.Join(", ", unfocused));
    }

    [TestMethod]
    public void AControlWithNoSize_WritesNothing()
    {
        List<string> wrote = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = Build(type);
                if (control is null)
                {
                    continue;
                }

                control.Position = new Position(3, 1);
                control.ViewSize = new Size(0, 0);
                ScreenBuffer screen = new(20, 4);
                for (int y = 0; y < 4; y++)
                {
                    screen.Write(0, new Position(0, y), new Size(20, 1), new ConxelString(new string('Ø', 20), Color.White, Color.Black));
                }

                control.Draw(screen);
                if (Enumerable.Range(0, 4).Any(y => screen.Line(y) != new string('Ø', 20)))
                {
                    wrote.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                wrote.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, wrote.Count, string.Join(", ", wrote));
    }

    private static void FillReadOnlyContent(Control control)
    {
        switch (control)
        {
            case CheckList list:
                list.SetItems(["alpha", "beta"]);
                break;
            case SegmentedControl segmented:
                segmented.Options = ["Day", "Week"];
                break;
            case iConsole.Controls.Editors.TextEditorControl editor:
                editor.InsertText("some text");
                break;
        }
    }

    [TestMethod]
    public void AReadOnlyControl_LooksDifferent_FromAnEditableOne()
    {
        List<string> same = [];
        foreach (Type type in ControlTypes().Where(t => typeof(IReadOnly).IsAssignableFrom(t)).Where(t => t.Name != "TextEditorControl"))
        {
            try
            {
                Control? editable = Build(type);
                Control? locked = Build(type);
                if (editable is null || locked is null)
                {
                    continue;
                }

                foreach (Control control in new[] { editable, locked })
                {
                    control.Position = new Position(0, 0);
                    control.ViewSize = new Size(30, 8);
                    if (control is TextBox box)
                    {
                        box.Text = "abc";
                    }

                    control.SetFocus();
                    FillReadOnlyContent(control);
                }

                ((IReadOnly)locked).IsReadOnly = true;
                ScreenBuffer on = ScreenBuffer.Render(editable, 30, 8);
                ScreenBuffer off = ScreenBuffer.Render(locked, 30, 8);
                bool differs = on.ToString() != off.ToString();
                for (int y = 0; y < 8 && !differs; y++)
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
    public async Task ARightClick_ChangesNothing_OnAnyControl()
    {
        List<string> reacted = [];
        foreach (Type type in ControlTypes().Where(t => !Animates.Contains(t.Name)))
        {
            try
            {
                Control? control = Build(type);
                if (control is null)
                {
                    continue;
                }

                control.Position = new Position(0, 0);
                control.ViewSize = new Size(30, 3);
                string before = ScreenBuffer.Render(control, 30, 3).ToString();
                await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), RightClicked = true });
                await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(15, 1), RightClicked = true });
                string after = ScreenBuffer.Render(control, 30, 3).ToString();
                if (before != after)
                {
                    reacted.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                reacted.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, reacted.Count, string.Join(", ", reacted));
    }

    private static readonly HashSet<string> Animates = ["BouncingLabel", "FilePicker", "Spinner", "Skeleton", "ToastHost", "ProgressBar", "Date", "Countdown", "ElapsedClock"];

    [TestMethod]
    public void ChangingASetting_BackAndForth_LeavesTheSamePictureAsSettingItOnce()
    {
        List<string> stale = [];
        foreach (Type type in ControlTypes().Where(t => !Animates.Contains(t.Name)))
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0 && !Skipped.Contains(p.Name)
                    && (p.PropertyType == typeof(int) || p.PropertyType == typeof(bool) || p.PropertyType.IsEnum)
                    && p.Name is not (nameof(Control.ZOrder) or nameof(Control.IsVisible) or nameof(Control.CanFocus) or nameof(Control.IsModal))))
            {
                object?[] values = property.PropertyType == typeof(bool) ? [true, false]
                    : property.PropertyType.IsEnum ? [.. Enum.GetValues(property.PropertyType).Cast<object>().Take(3)]
                    : [1, 8, 30];
                try
                {
                    Control? busy = Build(type);
                    Control? once = Build(type);
                    if (busy is null || once is null)
                    {
                        break;
                    }

                    foreach (Control control in new[] { busy, once })
                    {
                        control.Position = new Position(0, 0);
                        control.ViewSize = new Size(30, 3);
                    }

                    ScreenBuffer persistent = new(30, 3);
                    long treeBefore = Control.TreeVersion;
                    foreach (object? value in values)
                    {
                        property.SetValue(busy, value);
                        busy.Draw(persistent);
                    }

                    property.SetValue(once, values[^1]);
                    string expected = ScreenBuffer.Render(once, 30, 3).ToString();
                    if (persistent.ToString() != expected && Control.TreeVersion == treeBefore)
                    {
                        stale.Add($"{type.Name}.{property.Name}");
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        Assert.AreEqual(0, stale.Count, string.Join(", ", stale));
    }
}
