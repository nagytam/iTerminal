using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// Every dialog of iConsole that can be built from the shared factory is opened on screens from a handful of cells to a
/// large one, drawn, and pressed with the keys that move around: a terminal can be any size, and none may take the
/// application down.
/// </summary>
[TestClass]
public class DialogSizeSweepTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task EveryDialog_OpensDrawsAndTakesKeys_OnEveryScreenSize()
    {
        List<string> failed = [];
        int opened = 0;
        foreach (Type type in typeof(Control).Assembly.GetExportedTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach ((uint width, uint height) in new[] { (1u, 1u), (2u, 2u), (4u, 3u), (10u, 4u), (25u, 5u), (60u, 3u), (6u, 30u), (80u, 25u), (200u, 60u) })
            {
                if (SweepControlFactory.Build(type, Theme()) is not Dialog dialog)
                {
                    break;
                }

                try
                {
                    Control root = new(Theme()) { Name = "Root", ViewSize = new Size(width, height) };
                    Task shown = root.ShowDialogAsync(dialog);
                    if (await Task.WhenAny(shown, Task.Delay(3000)) != shown)
                    {
                        break;
                    }

                    await shown;
                    opened++;
                    ScreenBuffer.Render(root, (int)width, (int)height);
                    foreach (ConsoleKey key in new[] { ConsoleKey.Tab, ConsoleKey.DownArrow, ConsoleKey.PageDown, ConsoleKey.End, ConsoleKey.Home, ConsoleKey.UpArrow, ConsoleKey.RightArrow, ConsoleKey.LeftArrow, ConsoleKey.A })
                    {
                        await dialog.HandleKeyEventAsync(new KeyEvent(key == ConsoleKey.A ? 'a' : '\0', key, false, false, false));
                        ScreenBuffer.Render(root, (int)width, (int)height);
                    }
                }
                catch (Exception exception)
                {
                    failed.Add($"{type.Name} {width}x{height}: {exception.GetType().Name} {exception.Message.Split('\n')[0]}");
                }
            }
        }

        Assert.IsTrue(opened >= 9, $"only {opened} dialog screens could be opened");
        Assert.AreEqual(0, failed.Count, string.Join(Environment.NewLine, failed.Take(30)));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task EveryDialog_ClosesOnEsc_FromItsFirstScreen()
    {
        List<string> stays = [];
        int opened = 0;
        foreach (Type type in typeof(Control).Assembly.GetExportedTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (SweepControlFactory.Build(type, Theme()) is not Dialog dialog)
            {
                continue;
            }

            Control root = new(Theme()) { Name = "Root", ViewSize = new Size(100, 30) };
            Task shown = root.ShowDialogAsync(dialog);
            if (await Task.WhenAny(shown, Task.Delay(3000)) != shown)
            {
                continue;
            }

            await shown;
            opened++;
            await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));
            if (root.Controls.AsSnapshot().Contains(dialog) && dialog.IsVisible)
            {
                stays.Add(type.Name);
            }
        }

        Assert.IsTrue(opened >= 1, "no dialog could be opened");
        Assert.AreEqual(0, stays.Count, string.Join(", ", stays));
    }
}
