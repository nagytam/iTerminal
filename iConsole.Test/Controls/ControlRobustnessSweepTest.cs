using System.Reflection;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// Throws every key and a spread of clicks at each control that can be built from a theme service alone, at
/// awkward sizes and in each state, and draws it. None of that may throw: a terminal can be resized to a
/// handful of cells, and a stray key or click must never take the application down.
/// </summary>
[TestClass]
public class ControlRobustnessSweepTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static IEnumerable<Type> SimpleControlTypes() => typeof(Control).Assembly.GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(Control).IsAssignableFrom(t))
        .Where(t => t.Namespace is "iConsole.Controls" or "iConsole.Controls.Charts" or "iConsole.Controls.Progress")
        .Where(t => t.GetConstructor([typeof(IThemeService)]) is not null)
        .Where(t => t != typeof(iConsole.Controls.Experimental.BouncingLabel) && !typeof(Window).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static readonly (uint Width, uint Height)[] Sizes = [(0, 0), (1, 1), (2, 1), (3, 2), (7, 3), (40, 5), (200, 60)];

    private static readonly ConsoleKey[] Keys =
    [
        ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.LeftArrow, ConsoleKey.RightArrow, ConsoleKey.Home, ConsoleKey.End,
        ConsoleKey.PageUp, ConsoleKey.PageDown, ConsoleKey.Enter, ConsoleKey.Spacebar, ConsoleKey.Escape, ConsoleKey.Backspace,
        ConsoleKey.Delete, ConsoleKey.Tab, ConsoleKey.D5, ConsoleKey.A, ConsoleKey.F4,
    ];

    [TestMethod]
    public async Task EveryControl_SurvivesDrawKeysAndClicks_AtAwkwardSizes()
    {
        List<string> failures = [];
        foreach (Type type in SimpleControlTypes())
        {
            foreach ((uint width, uint height) in Sizes)
            {
                foreach (int state in new[] { 0, 1, 2 })
                {
                    try
                    {
                        Control control = (Control)Activator.CreateInstance(type, Theme())!;
                        control.Position = new Position(0, 0);
                        control.ViewSize = new Size(width, height);
                        control.IsEnabled = state != 1;
                        if (state == 2)
                        {
                            control.SetFocus();
                        }

                        ScreenBuffer buffer = new((int)Math.Max(1, width), (int)Math.Max(1, height));
                        control.Draw(buffer);
                        foreach (ConsoleKey key in Keys)
                        {
                            await control.HandleKeyEventAsync(new KeyEvent(key == ConsoleKey.A ? 'a' : key == ConsoleKey.D5 ? '5' : '\0', key, false, false, false));
                            control.Draw(buffer);
                        }

                        foreach ((long x, long y) in new[] { (0L, 0L), (1L, 0L), (width / 2L, height / 2L), (Math.Max(0L, width - 1L), Math.Max(0L, height - 1L)), (width + 5L, height + 5L) })
                        {
                            await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, y), LeftClicked = true });
                            await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, y), RightClicked = true });
                            await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, y), WheelDelta = 1 });
                            await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, y), WheelDelta = -1 });
                        }

                        control.Draw(buffer);
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{type.Name} {width}x{height} state {state}: {exception.GetType().Name}: {exception.Message}");
                    }
                }
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures.Distinct().Take(40)));
    }

    [TestMethod]
    public void TheSweepCoversTheSimpleControls()
    {
        List<Type> types = SimpleControlTypes().ToList();
        Assert.IsTrue(types.Count >= 30, $"only {types.Count} control types were found");
        CollectionAssert.IsSubsetOf(new[] { typeof(Slider), typeof(Rating), typeof(Calendar), typeof(Toolbar) }, types);
    }
}
