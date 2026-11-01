using System.Reflection;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Controls;

/// <summary>
/// The sweeps of the iConsole controls, for the controls of iTerminal itself that can be built from mocked services:
/// every public text or number setting is set to an awkward value, every one is drawn at awkward sizes and pressed.
/// </summary>
[TestClass]
public class CoreControlSweepTest
{
    private static object? Resolve(Type type, int depth)
    {
        if (type == typeof(IThemeService))
        {
            return TestTheme();
        }

        if (type == typeof(string))
        {
            return "text";
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type.IsInterface)
        {
            Mock mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;
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

    /// <summary>A mock cannot stand in for a class such as Url: the rig, not the control.</summary>
    private static bool IsRigNoise(Exception exception) => exception.Message.Contains("proxy", StringComparison.Ordinal) || exception.InnerException?.Message.Contains("proxy", StringComparison.Ordinal) == true;

    private static IEnumerable<Type> CoreControls() => typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
        .Where(t => typeof(Control).IsAssignableFrom(t) && !typeof(Window).IsAssignableFrom(t) && !typeof(MenuItem).IsAssignableFrom(t)
            && !t.IsAbstract && !t.IsGenericTypeDefinition)
        .OrderBy(t => t.FullName, StringComparer.Ordinal);

    [Timeout(60_000)]
    [TestMethod]
    public async Task EveryCoreControl_SurvivesAwkwardSizes_Keys_AndClicks()
    {
        List<string> failed = [];
        int built = 0;
        foreach (Type type in CoreControls())
        {
            foreach ((uint width, uint height) in new[] { (0u, 0u), (1u, 1u), (3u, 2u), (30u, 3u), (200u, 60u) })
            {
                try
                {
                    if (Resolve(type, 0) is not Control control)
                    {
                        break;
                    }

                    built++;
                    control.Position = new Position(0, 0);
                    control.ViewSize = new Size(width, height);
                    ScreenBuffer screen = new((int)Math.Max(1, width), (int)Math.Max(1, height));
                    control.Draw(screen);
                    control.SetFocus();
                    foreach (ConsoleKey key in new[] { ConsoleKey.DownArrow, ConsoleKey.Enter, ConsoleKey.Backspace, ConsoleKey.A, ConsoleKey.Delete, ConsoleKey.End })
                    {
                        await control.HandleKeyEventAsync(new KeyEvent(key == ConsoleKey.A ? 'a' : '\0', key, false, false, false));
                        control.Draw(screen);
                    }

                    await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });
                    await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), WheelDelta = 1 });
                    control.Draw(screen);
                }
                catch (Exception exception) when (IsRigNoise(exception))
                {
                }
                catch (Exception exception)
                {
                    failed.Add($"{type.Name} {width}x{height}: {exception.GetType().Name} {exception.Message.Split('\n')[0]}");
                }
            }
        }

        Assert.IsTrue(built >= 10, $"only {built} controls could be built");
        Assert.AreEqual(0, failed.Count, string.Join(Environment.NewLine, failed.Take(30)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryCoreControl_AcceptsNullAndHugeSettings()
    {
        List<string> failed = [];
        foreach (Type type in CoreControls())
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0
                    && p.Name is not (nameof(Control.Position) or nameof(Control.ViewSize) or nameof(Control.Parent) or nameof(Control.Name) or nameof(Control.IsVisible) or nameof(Control.CanFocus) or nameof(Control.ZOrder))
                    && (p.PropertyType == typeof(string) || p.PropertyType == typeof(int))))
            {
                foreach (object? value in property.PropertyType == typeof(string) ? new object?[] { null, string.Empty, new string('x', 400) } : [-1, int.MaxValue])
                {
                    try
                    {
                        if (Resolve(type, 0) is not Control control)
                        {
                            break;
                        }

                        control.Position = new Position(0, 0);
                        control.ViewSize = new Size(20, 3);
                        property.SetValue(control, value);
                        control.Draw(new ScreenBuffer(20, 3));
                    }
                    catch (Exception exception) when (IsRigNoise(exception))
                    {
                    }
                    catch (Exception exception)
                    {
                        Exception inner = exception is TargetInvocationException { InnerException: { } i } ? i : exception;
                        failed.Add($"{type.Name}.{property.Name} = {value ?? "null"}: {inner.GetType().Name} {inner.Message.Split('\n')[0]}");
                    }
                }
            }
        }

        Assert.AreEqual(0, failed.Count, string.Join(Environment.NewLine, failed.Take(30)));
    }
}
