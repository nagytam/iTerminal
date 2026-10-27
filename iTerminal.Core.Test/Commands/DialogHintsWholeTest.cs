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
            return ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!).Object;
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
}
