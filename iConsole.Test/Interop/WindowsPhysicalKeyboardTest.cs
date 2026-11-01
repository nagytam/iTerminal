using iConsole.Interop;

namespace iConsole.Test.Interop;

[TestClass]
public class WindowsPhysicalKeyboardTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ModifierQueries_ReturnFalseOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.IsInstanceOfType(WindowsPhysicalKeyboard.IsShiftHeld(), typeof(bool));
            Assert.IsInstanceOfType(WindowsPhysicalKeyboard.IsControlHeld(), typeof(bool));
            Assert.IsInstanceOfType(WindowsPhysicalKeyboard.IsAltHeld(), typeof(bool));
            return;
        }

        Assert.IsFalse(WindowsPhysicalKeyboard.IsShiftHeld());
        Assert.IsFalse(WindowsPhysicalKeyboard.IsControlHeld());
        Assert.IsFalse(WindowsPhysicalKeyboard.IsAltHeld());
    }
}
