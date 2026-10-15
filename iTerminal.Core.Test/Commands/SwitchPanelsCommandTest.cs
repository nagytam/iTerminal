using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Commands;

namespace iTerminal.Core.Commands.Test;

[TestClass]
public class SwitchPanelsCommandTest
{
    [TestMethod]
    public void Constructor_Default_ShouldCreateInstance()
    {
        // Arrange

        // Act
        var type = typeof(iTerminal.Core.Commands.SwitchPanelsCommand);
        object? instance = null;
        try { instance = Activator.CreateInstance(type); }
        catch
        {
            foreach (var c in type.GetConstructors())
            {
                var args = c.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                try { instance = c.Invoke(args); break; } catch { }
            }
        }

        Assert.IsNotNull(instance, "Could not instantiate iTerminal.Core.Commands.SwitchPanelsCommand");
    }
}

