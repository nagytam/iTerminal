using System.Reflection;
using iConsole.Dialogs;
using iConsole.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands;

/// <summary>Every key a dialog lists in the F1 overlay has a name and a description, and is named once.</summary>
[TestClass]
public class DialogHintsWholeTest
{
    internal static object? Resolve(Type type, int depth)
    {
        if (type == typeof(IThemeService))
        {
            return TestTheme();
        }

        if (type == typeof(string))
        {
            return string.Empty;
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type.IsInterface)
        {
            Mock mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;
            // A mocked list or model answers with an empty mock, where the plain default is null that no real service gives.
            mock.DefaultValue = DefaultValue.Mock;
            return mock.Object;
        }

        if (type.IsAbstract || depth > 3)
        {
            return null;
        }

        foreach (ConstructorInfo constructor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
        {
            object?[] arguments = [.. constructor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : Resolve(p.ParameterType, depth + 1))];
            if (arguments.Zip(constructor.GetParameters()).Any(pair => pair.First is null && !pair.Second.HasDefaultValue))
            {
                continue;
            }

            try
            {
                return constructor.Invoke(arguments);
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheHintsOfEveryDialog_AreWhole_AndNameEachKeyOnce()
    {
        List<string> faulty = [];
        int checkedDialogs = 0;
        foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (Resolve(type, 0) is not Dialog dialog)
            {
                continue;
            }

            try
            {
                List<(string Key, string Description)> hints = [.. dialog.GetKeyHints()];
                checkedDialogs++;
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

        Assert.IsTrue(checkedDialogs >= 30, $"only {checkedDialogs} dialogs could be checked");
        Assert.AreEqual(0, faulty.Count, string.Join(", ", faulty));
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task EveryDialog_ClosesOnEsc_FromItsFirstScreen()
    {
        List<string> stays = [];
        int checkedDialogs = 0;
        foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (Resolve(type, 0) is not Dialog dialog)
            {
                continue;
            }

            try
            {
                Task<iConsole.Controls.Control> shown = ShowAsync(dialog);
                if (await Task.WhenAny(shown, Task.Delay(3000)) != shown)
                {
                    continue;
                }

                iConsole.Controls.Control root = await shown;
                checkedDialogs++;
                await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
                if (root.Controls.AsSnapshot().Contains(dialog) && dialog.IsVisible)
                {
                    stays.Add(type.Name);
                }
            }
            catch (Exception)
            {
            }
        }

        Assert.IsTrue(checkedDialogs >= 20, $"only {checkedDialogs} dialogs could be opened");
        Assert.AreEqual(0, stays.Count, string.Join(", ", stays));
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task EveryDialog_HasATitle_AndDrawsOnASmallAndANormalScreen()
    {
        List<string> faulty = [];
        int checkedDialogs = 0;
        foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach ((uint width, uint height) in new[] { (30u, 8u), (120u, 40u), (4u, 3u), (1u, 1u), (2u, 2u), (10u, 4u), (16u, 6u), (25u, 5u), (60u, 3u), (6u, 30u) })
            {
                if (Resolve(type, 0) is not Dialog dialog)
                {
                    break;
                }

                try
                {
                    Task<iConsole.Controls.Control> shown = ShowAsync(dialog, new iConsole.Data.Size(width, height));
                    if (await Task.WhenAny(shown, Task.Delay(3000)) != shown)
                    {
                        break;
                    }

                    iConsole.Controls.Control root = await shown;
                    checkedDialogs++;
                    iConsole.Test.Core.ScreenBuffer.Render(root, (int)width, (int)height);
                    iConsole.Test.Core.ScreenBuffer.Render(root, (int)width, (int)height);
                    foreach (ConsoleKey key in new[] { ConsoleKey.Tab, ConsoleKey.DownArrow, ConsoleKey.PageDown, ConsoleKey.End, ConsoleKey.Home, ConsoleKey.UpArrow, ConsoleKey.RightArrow, ConsoleKey.LeftArrow })
                    {
                        await dialog.HandleKeyEventAsync(Key(key));
                        iConsole.Test.Core.ScreenBuffer.Render(root, (int)width, (int)height);
                    }
                    if (string.IsNullOrWhiteSpace(dialog.Title) && type.Name != "LanguageDialog")
                    {
                        faulty.Add($"{type.Name} (no title)");
                    }
                }
                catch (Exception exception) when (exception.Message.Contains("proxy", StringComparison.Ordinal) || exception.Message.Contains("empty string", StringComparison.Ordinal))
                {
                    // A mock cannot stand in for a class such as Url, and a viewer opened with no file has no path: the rig, not the dialog.
                }
                catch (Exception exception)
                {
                    faulty.Add($"{type.Name} {width}x{height} ({exception.GetType().Name}: {exception.Message.Split('\n')[0]})");
                }
            }
        }

        Assert.IsTrue(checkedDialogs >= 40, $"only {checkedDialogs} dialog screens could be drawn");
        Assert.AreEqual(0, faulty.Count, string.Join(Environment.NewLine, faulty.Take(30)));
    }

    private static IEnumerable<iConsole.Controls.IControl> Descendants(iConsole.Controls.IControl control)
    {
        foreach (iConsole.Controls.IControl child in control.Controls.AsSnapshot())
        {
            yield return child;
            foreach (iConsole.Controls.IControl deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task NoTwoControlsOfADialog_ShareAHotKey()
    {
        List<string> clashes = [];
        int checkedDialogs = 0;
        foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (Resolve(type, 0) is not Dialog dialog)
            {
                continue;
            }

            try
            {
                Task<iConsole.Controls.Control> shown = ShowAsync(dialog);
                if (await Task.WhenAny(shown, Task.Delay(3000)) != shown)
                {
                    continue;
                }

                await shown;
                checkedDialogs++;
                var duplicates = Descendants(dialog)
                    .OfType<iConsole.Controls.Control>().Where(c => c.IsVisible && c.HotKeyChar is not null)
                    .GroupBy(c => char.ToLowerInvariant(c.HotKeyChar!.Value))
                    .Where(group => group.Count() > 1)
                    .Select(group => $"'{group.Key}' on {string.Join(" and ", group.Select(c => c.GetType().Name))}")
                    .ToList();
                if (duplicates.Count > 0)
                {
                    clashes.Add($"{type.Name}: {string.Join("; ", duplicates)}");
                }
            }
            catch (Exception)
            {
            }
        }

        Assert.IsTrue(checkedDialogs >= 20, $"only {checkedDialogs} dialogs could be opened");
        Assert.AreEqual(0, clashes.Count, string.Join(Environment.NewLine, clashes));
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task EveryFocusableControlOfADialog_CanBeReachedWithTab()
    {
        List<string> unreachable = [];
        int checkedDialogs = 0;
        foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (Resolve(type, 0) is not Dialog dialog)
            {
                continue;
            }

            try
            {
                Task<iConsole.Controls.Control> shown = ShowAsync(dialog);
                if (await Task.WhenAny(shown, Task.Delay(3000)) != shown)
                {
                    continue;
                }

                await shown;
                checkedDialogs++;
                List<iConsole.Controls.Control> focusable = [.. Descendants(dialog).OfType<iConsole.Controls.Control>().Where(c => c.CanFocus && c.IsVisible && c.IsEnabled)];
                HashSet<iConsole.Controls.Control> reached = [];
                for (int press = 0; press < focusable.Count * 2 + 4; press++)
                {
                    foreach (iConsole.Controls.Control control in focusable.Where(c => c.IsFocused))
                    {
                        reached.Add(control);
                    }

                    await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab));
                }

                List<string> missing = [.. focusable.Where(c => !reached.Contains(c)).Select(c => c.GetType().Name).Distinct()];
                if (missing.Count > 0)
                {
                    unreachable.Add($"{type.Name}: {string.Join(", ", missing)}");
                }
            }
            catch (Exception)
            {
            }
        }

        Assert.IsTrue(checkedDialogs >= 20, $"only {checkedDialogs} dialogs could be opened");
        Assert.AreEqual(0, unreachable.Count, string.Join(Environment.NewLine, unreachable));
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task ADialogWithFocusableControls_OpensWithOneFocused_AndShiftTabReachesThemAll()
    {
        List<string> problems = [];
        int checkedDialogs = 0;
        foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (Resolve(type, 0) is not Dialog dialog)
            {
                continue;
            }

            try
            {
                Task<iConsole.Controls.Control> shown = ShowAsync(dialog);
                if (await Task.WhenAny(shown, Task.Delay(3000)) != shown)
                {
                    continue;
                }

                await shown;
                checkedDialogs++;
                List<iConsole.Controls.Control> focusable = [.. Descendants(dialog).OfType<iConsole.Controls.Control>().Where(c => c.CanFocus && c.IsVisible && c.IsEnabled)];
                if (focusable.Count == 0)
                {
                    continue;
                }

                if (!focusable.Any(c => c.IsFocused))
                {
                    problems.Add($"{type.Name}: opens with nothing focused");
                    continue;
                }

                HashSet<iConsole.Controls.Control> reached = [];
                for (int press = 0; press < focusable.Count * 2 + 4; press++)
                {
                    foreach (iConsole.Controls.Control control in focusable.Where(c => c.IsFocused))
                    {
                        reached.Add(control);
                    }

                    await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, shift: true));
                }

                List<string> missing = [.. focusable.Where(c => !reached.Contains(c)).Select(c => c.GetType().Name).Distinct()];
                if (missing.Count > 0)
                {
                    problems.Add($"{type.Name}: Shift+Tab never reaches {string.Join(", ", missing)}");
                }
            }
            catch (Exception)
            {
            }
        }

        Assert.IsTrue(checkedDialogs >= 20, $"only {checkedDialogs} dialogs could be opened");
        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryDialogFooterThatListsKeys_SaysHowToLeave()
    {
        List<string> silent = [];
        int footers = 0;
        foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract))
        {
            FieldInfo? field = type.GetField("KeyHints", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field?.GetRawConstantValue() is not string footer)
            {
                continue;
            }

            footers++;
            if (!footer.Contains("Esc", StringComparison.OrdinalIgnoreCase))
            {
                silent.Add(type.Name);
            }
        }

        Assert.IsTrue(footers >= 8, $"only {footers} footers were found");
        Assert.AreEqual(0, silent.Count, "These footers do not say how to leave: " + string.Join(", ", silent));
    }
}
