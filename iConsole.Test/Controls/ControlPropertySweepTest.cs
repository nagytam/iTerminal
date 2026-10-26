using System.Reflection;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// Sets every public setting of each simple control (a number, a text, a flag, a choice) to an awkward value —
/// null, negative, huge, NaN — and draws it. A setting changed by a host at run time must never make the control
/// throw, whether in the setter or in the next draw.
/// </summary>
[TestClass]
public class ControlPropertySweepTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static readonly HashSet<string> Skipped =
    [
        nameof(Control.Position), nameof(Control.ViewSize), nameof(Control.Parent), nameof(Control.ZOrder), nameof(Control.Name),
        nameof(Control.IsVisible), nameof(Control.CanFocus),
    ];

    /// <summary>The controls to sweep; one that is generic over a single type is swept closed over string.</summary>
    private static readonly ConsoleKey[] Keys =
    [
        ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.LeftArrow, ConsoleKey.RightArrow, ConsoleKey.Home, ConsoleKey.End,
        ConsoleKey.PageUp, ConsoleKey.PageDown, ConsoleKey.Enter, ConsoleKey.Spacebar, ConsoleKey.Backspace, ConsoleKey.Delete,
        ConsoleKey.D5, ConsoleKey.A,
    ];

    private static IEnumerable<Type> ControlTypes() => typeof(Control).Assembly.GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(Control).IsAssignableFrom(t))
        .Where(t => t.Namespace is not null && t.Namespace.StartsWith("iConsole.Controls", StringComparison.Ordinal))
        .Where(t => !typeof(Window).IsAssignableFrom(t))
        .Where(t => !t.IsGenericTypeDefinition || t.GetGenericArguments().Length == 1)
        .Select(t => t.IsGenericTypeDefinition ? TryClose(t) : t)
        .OfType<Type>()
        .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static Type? TryClose(Type definition)
    {
        try
        {
            return definition.MakeGenericType(typeof(string));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Control? Build(Type type)
    {
        foreach (ConstructorInfo constructor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
        {
            object?[] arguments = new object?[constructor.GetParameters().Length];
            bool resolved = true;
            for (int i = 0; i < arguments.Length && resolved; i++)
            {
                Type parameter = constructor.GetParameters()[i].ParameterType;
                if (parameter == typeof(IThemeService))
                {
                    arguments[i] = Theme();
                }
                else if (parameter.IsInterface && parameter.Name.EndsWith("Service", StringComparison.Ordinal))
                {
                    arguments[i] = ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(parameter))!).Object;
                }
                else if (parameter.IsGenericType && parameter.GetGenericTypeDefinition().Name.StartsWith("IListBoxModel", StringComparison.Ordinal))
                {
                    Type model = typeof(iConsole.Model.ListBoxModel<>).MakeGenericType(parameter.GetGenericArguments());
                    arguments[i] = Activator.CreateInstance(model, Activator.CreateInstance(model.GetConstructors()[0].GetParameters()[0].ParameterType));
                }
                else if (!parameter.IsInterface && !parameter.IsAbstract && parameter.GetConstructor(Type.EmptyTypes) is not null)
                {
                    arguments[i] = Activator.CreateInstance(parameter);
                }
                else
                {
                    resolved = false;
                }
            }

            if (resolved)
            {
                return (Control)constructor.Invoke(arguments);
            }
        }

        return null;
    }

    private static IEnumerable<object?> ValuesFor(Type type)
    {
        if (type == typeof(int))
        {
            return [-1, int.MinValue, int.MaxValue, 0, 1, 2, 1000];
        }

        if (type == typeof(long))
        {
            return [-1L, long.MinValue, long.MaxValue];
        }

        if (type == typeof(double))
        {
            return [double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1.0];
        }

        if (type == typeof(string))
        {
            return [null, string.Empty, new string('x', 500), "{0}", " ", "a\nb\tc", "日本語\U0001F600"];
        }

        if (type == typeof(char))
        {
            return ['\0'];
        }

        if (type.IsEnum)
        {
            return [Enum.ToObject(type, 999)];
        }

        // A host with nothing to show passes null for a list; the list setters take it as empty.
        if (!type.IsValueType && (type.IsArray || type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)))
        {
            return [null];
        }

        return [];
    }

    [TestMethod]
    public void NoSetting_ThrowsOrBreaksTheNextDraw_ForAnAwkwardValue()
    {
        List<string> failures = [];
        foreach (Type type in ControlTypes())
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true } && !Skipped.Contains(p.Name) && p.GetIndexParameters().Length == 0))
            {
                foreach (object? value in ValuesFor(property.PropertyType))
                {
                    foreach ((uint width, uint height) in new[] { (20u, 3u), (3u, 1u) })
                    {
                        try
                        {
                            Control? control = Build(type);
                            if (control is null)
                            {
                                goto nextType;
                            }

                            control.Position = new Position(0, 0);
                            control.ViewSize = new Size(width, height);
                            property.SetValue(control, value);
                            ScreenBuffer buffer = new((int)width, (int)height);
                            control.Draw(buffer);
                            control.SetFocus();
                            foreach (ConsoleKey key in Keys)
                            {
                                control.HandleKeyEventAsync(new KeyEvent(key == ConsoleKey.A ? 'a' : key == ConsoleKey.D5 ? '5' : '\0', key, false, false, false)).GetAwaiter().GetResult();
                                control.Draw(buffer);
                            }

                            foreach ((long x, long y) in new[] { (0L, 0L), (width / 2L, height / 2L), (width + 3L, height + 3L) })
                            {
                                control.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, y), LeftClicked = true }).GetAwaiter().GetResult();
                                control.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, y), WheelDelta = 1 }).GetAwaiter().GetResult();
                            }

                            control.Draw(buffer);
                        }
                        catch (Exception exception)
                        {
                            Exception inner = exception is TargetInvocationException { InnerException: { } i } ? i : exception;
                            failures.Add($"{type.Name}.{property.Name} = {value ?? "null"} at {width}x{height}: {inner.GetType().Name} {inner.Message.Split('\n')[0]}");
                        }
                    }
                }
            }

            nextType:;
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures.Take(60)));
    }
}
