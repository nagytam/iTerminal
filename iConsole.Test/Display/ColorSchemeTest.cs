using iConsole.Data;
using iConsole.Display;

namespace iConsole.Test.Display;

[TestClass]
public class ColorSchemeTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeConstructor()
    {
        var colorScheme = new ColorScheme();
        Assert.IsNotNull(colorScheme);
        Assert.AreEqual(Color.Blue, colorScheme.Background);
        Assert.AreEqual(Color.White, colorScheme.Foreground);
        Assert.AreEqual(Theme.Default, colorScheme.Theme);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeDefaultColors()
    {
        var colorScheme = new ColorScheme();
        Assert.AreEqual(Color.Blue, colorScheme.Background);
        Assert.AreEqual(Color.White, colorScheme.Foreground);
        Assert.AreEqual(Color.Yellow, colorScheme.SelectedBackground);
        Assert.AreEqual(Color.Blue, colorScheme.SelectedForeground);
        Assert.AreEqual(Color.Red, colorScheme.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetBackground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.Background = Color.Black;
        Assert.AreEqual(Color.Black, colorScheme.Background);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetForeground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.Foreground = Color.Green;
        Assert.AreEqual(Color.Green, colorScheme.Foreground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetSelectedForeground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.SelectedForeground = Color.Magenta;
        Assert.AreEqual(Color.Magenta, colorScheme.SelectedForeground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetSelectedBackground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.SelectedBackground = Color.Cyan;
        Assert.AreEqual(Color.Cyan, colorScheme.SelectedBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetSelectedFocusedForeground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.SelectedFocusedForeground = Color.Red;
        Assert.AreEqual(Color.Red, colorScheme.SelectedFocusedForeground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetSelectedFocusedBackground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.SelectedFocusedBackground = Color.Green;
        Assert.AreEqual(Color.Green, colorScheme.SelectedFocusedBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetDisabledForeground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.DisabledForeground = Color.Gray;
        Assert.AreEqual(Color.Gray, colorScheme.DisabledForeground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetDisabledSelectedForeground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.DisabledSelectedForeground = Color.DarkGray;
        Assert.AreEqual(Color.DarkGray, colorScheme.DisabledSelectedForeground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetDisabledBackground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.DisabledBackground = Color.DarkBlue;
        Assert.AreEqual(Color.DarkBlue, colorScheme.DisabledBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetButtonBackground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.ButtonBackground = Color.DarkGreen;
        Assert.AreEqual(Color.DarkGreen, colorScheme.ButtonBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetDisabledButtonBackground()
    {
        var colorScheme = new ColorScheme();
        colorScheme.DisabledButtonBackground = Color.DarkRed;
        Assert.AreEqual(Color.DarkRed, colorScheme.DisabledButtonBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetError()
    {
        var colorScheme = new ColorScheme();
        colorScheme.Error = Color.Yellow;
        Assert.AreEqual(Color.Yellow, colorScheme.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeSetTheme()
    {
        var colorScheme = new ColorScheme();
        colorScheme.Theme = Theme.Dark;
        Assert.AreEqual(Theme.Dark, colorScheme.Theme);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeAllColorsSet()
    {
        var colorScheme = new ColorScheme();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorSchemeMultipleInstances()
    {
        var colorScheme1 = new ColorScheme();
        var colorScheme2 = new ColorScheme();

        colorScheme1.Background = Color.Red;

        Assert.AreEqual(Color.Red, colorScheme1.Background);
        Assert.AreEqual(Color.Blue, colorScheme2.Background);
    }
}
