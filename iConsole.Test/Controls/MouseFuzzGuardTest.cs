using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// The terminal reports the pointer wherever it is, including cells outside a control, on its edge or past what the control
/// last drew. Whatever arrives, a control must neither throw nor leave a picture that cannot be drawn.
/// </summary>
[TestClass]
public class MouseFuzzGuardTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static IEnumerable<Type> ControlTypes() => typeof(Control).Assembly.GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(Control).IsAssignableFrom(t) && !t.IsGenericTypeDefinition
            && t.Namespace is not null && t.Namespace.StartsWith("iConsole.Controls", StringComparison.Ordinal))
        .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static IEnumerable<MouseEvent> Events(long x, long y)
    {
        Position at = new(x, y);
        yield return new MouseEvent { Position = at, LeftClicked = true };
        yield return new MouseEvent { Position = at, LeftClicked = true, IsRelease = true };
        yield return new MouseEvent { Position = at, RightClicked = true };
        yield return new MouseEvent { Position = at, IsDrag = true, LeftClicked = true };
        yield return new MouseEvent { Position = at, IsHover = true };
        yield return new MouseEvent { Position = at, WheelDelta = 1 };
        yield return new MouseEvent { Position = at, WheelDelta = -1 };
    }

    [TestMethod]
    public async Task AnyPointerEvent_AtAnyCell_NeverThrows_AndTheControlStillDraws()
    {
        List<string> failures = [];
        (long, long)[] cells = [(0, 0), (1, 0), (5, 1), (14, 1), (29, 3), (30, 4), (31, 5), (200, 200), (int.MaxValue, int.MaxValue), (0, 3)];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = SweepControlFactory.Build(type, Theme());
                if (control is null)
                {
                    continue;
                }

                control.Position = new Position(0, 0);
                control.ViewSize = new Size(30, 4);
                foreach ((long x, long y) in cells)
                {
                    foreach (MouseEvent mouseEvent in Events(x, y))
                    {
                        await control.HandleMouseEventAsync(mouseEvent);
                    }

                    ScreenBuffer.Render(control, 30, 4);
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{type.Name} ({exception.GetType().Name}: {exception.Message})");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join("; ", failures));
    }

    [TestMethod]
    public async Task AClick_LeavesTheControlDrawingTheSameTwice()
    {
        List<string> failures = [];
        foreach (Type type in ControlTypes())
        {
            try
            {
                Control? control = SweepControlFactory.Build(type, Theme());
                if (control is null)
                {
                    continue;
                }

                control.Position = new Position(0, 0);
                control.ViewSize = new Size(30, 4);
                await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 1), LeftClicked = true });
                await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 1), LeftClicked = true, IsRelease = true });
                string first = ScreenBuffer.Render(control, 30, 4).ToString();
                string second = ScreenBuffer.Render(control, 30, 4).ToString();
                if (first != second)
                {
                    failures.Add(type.Name);
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{type.Name} ({exception.GetType().Name})");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(", ", failures));
    }
}
