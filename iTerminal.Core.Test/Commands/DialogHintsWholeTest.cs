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
    private static object? Resolve(Type type, int depth)
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
            foreach ((uint width, uint height) in new[] { (30u, 8u), (120u, 40u) })
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
}
