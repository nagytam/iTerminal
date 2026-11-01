using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Layouts;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// A container hands its children rectangles inside its own, whatever size it is given, and lays them out again when it
/// is resized: a child drawn past the edge paints over its neighbour, and a layout fixed at the first size breaks the
/// moment the terminal is resized.
/// </summary>
[TestClass]
public class ContainerLayoutGuardTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static Label Child(string text) => new(text, Theme()) { ViewSize = new Size(8, 1) };

    private static IEnumerable<(string Name, Control Container)> Containers()
    {
        StackPanel stack = new(Theme());
        stack.Add(Child("one"));
        stack.Add(Child("two"));
        yield return ("StackPanel", stack);

        GridPanel grid = new(Theme());
        grid.SetColumns(SizeRule.Fill(), SizeRule.Fill());
        grid.SetRows(SizeRule.Fill());
        grid.Add(Child("a"), 0, 0);
        grid.Add(Child("b"), 0, 1);
        yield return ("GridPanel", grid);

        DockPanel dock = new(Theme());
        dock.Add(Child("top"), DockSide.Top);
        dock.Add(Child("left"), DockSide.Left);
        yield return ("DockPanel", dock);

        BoxPanel box = new(Theme());
        box.SetContent(Child("boxed"));
        yield return ("BoxPanel", box);

        GroupBox group = new(Theme());
        group.SetContent(Child("grouped"));
        yield return ("GroupBox", group);

        Splitter splitter = new(Theme());
        splitter.SetFirst(Child("first"));
        splitter.SetSecond(Child("second"));
        yield return ("Splitter", splitter);

        foreach (Type type in new[] { typeof(iConsole.Controls.StatusBar.ViewerStatusBar), typeof(iConsole.Controls.StatusBar.EditorStatusBar) })
        {
            if (SweepControlFactory.Build(type, Theme()) is { } bar)
            {
                yield return (type.Name, bar);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheChildren_StayInsideTheContainer_AtEverySize_AfterEveryResize()
    {
        List<string> outside = [];
        foreach ((string name, Control container) in Containers())
        {
            container.Position = new Position(0, 0);
            foreach ((uint width, uint height) in new[] { (30u, 6u), (10u, 3u), (3u, 2u), (1u, 1u), (40u, 10u), (6u, 2u) })
            {
                container.ViewSize = new Size(width, height);
                container.OnInitializeLayout();
                ScreenBuffer.Render(container, (int)Math.Max(1, width), (int)Math.Max(1, height));
                foreach (IControl child in container.Controls.AsSnapshot().Where(c => c.IsVisible))
                {
                    bool inside = (long)child.Position.X >= 0 && (long)child.Position.Y >= 0
                        && (long)child.Position.X + (long)child.ViewSize.Width <= width
                        && (long)child.Position.Y + (long)child.ViewSize.Height <= height;
                    if (!inside && child.ViewSize.Width > 0 && child.ViewSize.Height > 0)
                    {
                        outside.Add($"{name} {width}x{height}: child at {child.Position.X},{child.Position.Y} size {child.ViewSize.Width}x{child.ViewSize.Height}");
                    }
                }
            }
        }

        Assert.AreEqual(0, outside.Count, string.Join(Environment.NewLine, outside.Take(20)));
    }
}
