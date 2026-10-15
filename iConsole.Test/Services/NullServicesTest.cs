using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class NullServicesTest
{
    [TestMethod]
    public void NullConsoleDriver_Instance_ReturnsSingletonWithZeroWindowSize()
    {
        NullConsoleDriver driver = NullConsoleDriver.Instance;

        Assert.AreSame(driver, NullConsoleDriver.Instance);
        Assert.AreEqual(new Size(0, 0), driver.WindowSize);
        Assert.AreEqual(new Position(0, 0), driver.GetCursorPosition());
    }

    [TestMethod]
    public void NullConsoleDriver_Methods_DoNotThrow()
    {
        NullConsoleDriver driver = NullConsoleDriver.Instance;

        driver.Title = "title";
        driver.ForegroundColor = Color.White;
        driver.BackgroundColor = Color.Black;
        driver.IsCursorVisible = true;
        driver.Beep();
        driver.Clear();
        driver.RefreshWindowSize();
        driver.SetCursorPosition(1, 2);
        driver.Write(0, 0, new Conxel('A', Color.White, Color.Black));
        driver.Write(0, 0, new ConxelString("text", Color.White, Color.Black));

        Assert.AreEqual("title", driver.Title);
        Assert.AreEqual(Color.White, driver.ForegroundColor);
        Assert.AreEqual(Color.Black, driver.BackgroundColor);
        Assert.IsTrue(driver.IsCursorVisible);
    }

    [TestMethod]
    public void NullThemeService_Instance_ReturnsSingletonWithDefaultScheme()
    {
        NullThemeService service = NullThemeService.Instance;

        Assert.AreSame(service, NullThemeService.Instance);
        Assert.AreEqual(Color.Black, service.CurrentColorScheme.Background);
        Assert.AreEqual(Color.White, service.CurrentColorScheme.Foreground);
        Assert.IsTrue(service.ColorSchemes.ContainsKey(Theme.Default));
    }
}
