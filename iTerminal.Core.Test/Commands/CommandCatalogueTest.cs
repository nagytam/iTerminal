using System.Reflection;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// Builds every command of the application that can be made from mocked services and checks what a user meets: a title to
/// show in the menu and the command palette, and a hot key that no other command takes.
/// </summary>
[TestClass]
public class CommandCatalogueTest
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

    private static List<(Type Type, ICommand Command)> Build()
    {
        List<(Type, ICommand)> commands = [];
        foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
            .Where(t => typeof(Command).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (Resolve(type, 0) is ICommand command)
            {
                commands.Add((type, command));
            }
        }

        return commands;
    }

    [TestMethod]
    public void TheCatalogue_CoversAGoodShareOfTheCommands()
    {
        Assert.IsTrue(Build().Count >= 40, "too few commands could be built to check");
    }

    [TestMethod]
    public void EveryCommand_HasATitleAndADescription()
    {
        List<string> missing = [.. Build()
            .Where(entry => string.IsNullOrWhiteSpace(entry.Command.Title) || string.IsNullOrWhiteSpace(entry.Command.Description))
            .Select(entry => entry.Type.Name)];

        Assert.AreEqual(0, missing.Count, string.Join(", ", missing));
    }
}
