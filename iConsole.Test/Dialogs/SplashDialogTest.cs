using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Dialogs;
using iConsole.Controls;
using iConsole.Services;

namespace iConsole.Dialogs.Test;

[TestClass]
public class SplashDialogTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitializeAsync_ShouldMakeDialogVisible()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var parent = new Control(mockTheme.Object);
        var dlg = new SplashDialog(mockTheme.Object)
        {
            Parent = parent
        };

        await dlg.OnInitializeAsync();

        Assert.IsTrue(dlg.IsVisible);
    }

    /// <summary>
    /// Esc closes the splash and its timer closes it again three seconds later. The second close used
    /// to pop the focus entry of whatever dialog had been opened in between.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingTwiceLeavesTheNextDialogsFocusAlone()
    {
        var theme = new iConsole.Test.Core.TestThemeService();
        var root = new Control(theme) { ViewSize = new iConsole.Data.Size(80, 25) };
        var field = new Control(theme) { CanFocus = true };
        root.AddControl(field);
        field.SetFocus();
        var splash = new SplashDialog(theme);
        await field.ShowDialogAsync(splash);
        splash.Close();                         // Esc

        var next = new Dialog("Next", theme);
        await field.ShowDialogAsync(next);
        field.ResetFocus();
        splash.Close();                         // the timer, later

        Assert.IsFalse(field.IsFocused, "the timer's close must not hand focus back early");
        next.Close();
        Assert.IsTrue(field.IsFocused, "the next dialog still returns focus to the field");
    }
}
