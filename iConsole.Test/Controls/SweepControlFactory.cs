using System.Reflection;
using iConsole.Controls;
using iConsole.Commands;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// Builds a control of any type the sweeps cover, from the theme and whatever else its constructor asks for: a mock for a
/// service, a working command, a clock, an empty list or data source, a short text. A type that still cannot be built
/// returns null and the sweep skips it, so what a sweep guards grows with what this can make.
/// </summary>
internal static class SweepControlFactory
{
    public static Control? Build(Type type, IThemeService theme)
    {
        foreach (ConstructorInfo constructor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
        {
            object?[] arguments = [.. constructor.GetParameters().Select(p => Resolve(p.ParameterType, theme, 0))];
            if (arguments.Zip(constructor.GetParameters()).Any(pair => pair.First is null && !pair.Second.ParameterType.IsClass))
            {
                continue;
            }

            if (arguments.Any(a => a is null))
            {
                continue;
            }

            try
            {
                return (Control)constructor.Invoke(arguments);
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    private static object? Resolve(Type type, IThemeService theme, int depth)
    {
        if (type == typeof(IThemeService))
        {
            return theme;
        }

        if (type == typeof(string))
        {
            return "text";
        }

        if (type == typeof(iConsole.Controls.IFilePickerSource))
        {
            return new LocalFilePickerSource();
        }

        if (type == typeof(ITimeProvider))
        {
            return new SystemTimeProvider();
        }

        if (type == typeof(ICommand))
        {
            Mock<ICommand> command = new();
            command.Setup(c => c.CanExecute()).Returns(true);
            command.SetupGet(c => c.Title).Returns("Do");
            return command.Object;
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            Type[] arguments = type.GetGenericArguments();
            if (definition.Name.StartsWith("IListBoxModel", StringComparison.Ordinal))
            {
                Type model = typeof(iConsole.Model.ListBoxModel<>).MakeGenericType(arguments);
                object items = Activator.CreateInstance(model.GetConstructors()[0].GetParameters()[0].ParameterType)!;
                if (arguments[0] == typeof(string))
                {
                    foreach (string text in new[] { "alpha", "beta", "gamma" })
                    {
                        items.GetType().GetMethod("Add")!.Invoke(items, [text]);
                    }
                }

                return Activator.CreateInstance(model, items);
            }

            if (definition.Name.StartsWith("IVirtualDataSource", StringComparison.Ordinal))
            {
                object list = Activator.CreateInstance(typeof(List<>).MakeGenericType(arguments))!;
                if (arguments[0] == typeof(string))
                {
                    ((List<string>)list).AddRange(["alpha", "beta", "gamma"]);
                }

                return Activator.CreateInstance(typeof(iConsole.Model.ListDataSource<>).MakeGenericType(arguments), list);
            }
        }

        if (type.IsInterface && FindImplementation(type, theme, depth) is { } implementation)
        {
            return implementation;
        }

        if (type.IsInterface)
        {
            return type.Name.EndsWith("Service", StringComparison.Ordinal)
                ? ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!).Object
                : null;
        }

        if (type.IsAbstract || depth > 2)
        {
            return null;
        }

        foreach (ConstructorInfo constructor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
        {
            object?[] arguments = [.. constructor.GetParameters().Select(p => Resolve(p.ParameterType, theme, depth + 1))];
            if (arguments.Any(a => a is null))
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

    /// <summary>The one concrete class of iConsole that implements <paramref name="contract"/> and can be built from what this can make.</summary>
    private static object? FindImplementation(Type contract, IThemeService theme, int depth)
    {
        if (depth > 1 || contract.IsGenericType)
        {
            return null;
        }

        List<Type> candidates = [.. typeof(Control).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition && !typeof(Control).IsAssignableFrom(t) && contract.IsAssignableFrom(t))];
        if (candidates.Count != 1)
        {
            return null;
        }

        return Resolve(candidates[0], theme, depth + 1);
    }
}
