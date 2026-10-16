using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Tabs;

[TestClass]
public class TabPageTest
{
    private Mock<IThemeService> CreateMockThemeService(ColorScheme? colorScheme = null)
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme ?? new ColorScheme());
        return mockThemeService;
    }

    #region Creation and Basic Tests

    [TestMethod]
    public void TabPageCanBeCreated()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        Assert.IsNotNull(tabPage);
        Assert.IsInstanceOfType(tabPage, typeof(Control));
    }

    [TestMethod]
    public void TabPageHasName()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("MyTab", mockThemeService.Object);

        Assert.AreEqual("MyTab", tabPage.Name);
    }

    [TestMethod]
    public void TabPageNameCanBeEmpty()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("", mockThemeService.Object);

        Assert.AreEqual("", tabPage.Name);
    }

    [TestMethod]
    public void TabPageNameCanContainSpecialCharacters()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab #1 (Active) [Info]", mockThemeService.Object);

        Assert.AreEqual("Tab #1 (Active) [Info]", tabPage.Name);
    }

    [TestMethod]
    public void TabPageNameCanBeLong()
    {
        var mockThemeService = CreateMockThemeService();
        var longName = new string('A', 256);
        var tabPage = new TabPage(longName, mockThemeService.Object);

        Assert.AreEqual(longName, tabPage.Name);
    }

    #endregion

    #region Color Tests

    [TestMethod]
    public void TabPageGetColorEnabledAndFocused()
    {
        var mockThemeService = CreateMockThemeService();
        var colorScheme = new ColorScheme
        {
            SelectedFocusedForeground = Color.Red,
            SelectedBackground = Color.Blue
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme);

        var tabPage = new TabPage("Tab1", mockThemeService.Object)
        {
            IsEnabled = true,
            CanFocus = true
        };

        tabPage.SetFocus();
        tabPage.GetColor(out var foreground, out var background);

        Assert.AreEqual(Color.Red, foreground);
        Assert.AreEqual(Color.Blue, background);
    }

    [TestMethod]
    public void TabPageGetColorEnabledButNotFocused()
    {
        var mockThemeService = CreateMockThemeService();
        var colorScheme = new ColorScheme
        {
            Foreground = Color.Yellow,
            Background = Color.Black
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme);

        var tabPage = new TabPage("Tab1", mockThemeService.Object)
        {
            IsEnabled = true
        };

        tabPage.GetColor(out var foreground, out var background);

        Assert.AreEqual(Color.Yellow, foreground);
        Assert.AreEqual(Color.Black, background);
    }

    [TestMethod]
    public void TabPageGetColorDisabled()
    {
        var mockThemeService = CreateMockThemeService();
        var colorScheme = new ColorScheme
        {
            DisabledForeground = Color.Gray,
            DisabledBackground = Color.DarkGray
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme);

        var tabPage = new TabPage("Tab1", mockThemeService.Object)
        {
            IsEnabled = false
        };

        tabPage.GetColor(out var foreground, out var background);

        Assert.AreEqual(Color.Gray, foreground);
        Assert.AreEqual(Color.DarkGray, background);
    }

    [TestMethod]
    public void TabPageGetColorDisabledIgnoresFocus()
    {
        var mockThemeService = CreateMockThemeService();
        var colorScheme = new ColorScheme
        {
            DisabledForeground = Color.Gray,
            DisabledBackground = Color.DarkGray,
            SelectedFocusedForeground = Color.Red,
            SelectedBackground = Color.Blue
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme);

        var tabPage = new TabPage("Tab1", mockThemeService.Object)
        {
            IsEnabled = false,
            CanFocus = true
        };

        tabPage.SetFocus();
        tabPage.GetColor(out var foreground, out var background);

        Assert.AreEqual(Color.Gray, foreground);
        Assert.AreEqual(Color.DarkGray, background);
    }

    [TestMethod]
    public void TabPageGetColorUsesThemeServiceColorScheme()
    {
        var mockThemeService = CreateMockThemeService();
        var colorScheme1 = new ColorScheme
        {
            Foreground = Color.White,
            Background = Color.Black
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme1);

        var tabPage = new TabPage("Tab1", mockThemeService.Object) { IsEnabled = true };
        tabPage.GetColor(out var foreground, out var background);

        Assert.AreEqual(Color.White, foreground);
        Assert.AreEqual(Color.Black, background);

        // Change the color scheme
        var colorScheme2 = new ColorScheme
        {
            Foreground = Color.Green,
            Background = Color.Blue
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme2);
        tabPage.GetColor(out foreground, out background);

        Assert.AreEqual(Color.Green, foreground);
        Assert.AreEqual(Color.Blue, background);
    }

    #endregion

    #region State Tests

    [TestMethod]
    public void TabPageHasDefaultVisibility()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        Assert.IsTrue(tabPage.IsVisible);
    }

    [TestMethod]
    public void TabPageHasDefaultEnabled()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        Assert.IsTrue(tabPage.IsEnabled);
    }

    [TestMethod]
    public void TabPageCanBeDisabled()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        tabPage.IsEnabled = false;

        Assert.IsFalse(tabPage.IsEnabled);
    }

    [TestMethod]
    public void TabPageCanBeHidden()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        tabPage.IsVisible = false;

        Assert.IsFalse(tabPage.IsVisible);
    }

    [TestMethod]
    public void TabPageCanBeFocused()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object) { CanFocus = true };

        tabPage.SetFocus();

        Assert.IsTrue(tabPage.IsFocused);
    }

    [TestMethod]
    public void TabPageFocusCanBeReset()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object) { CanFocus = true };

        tabPage.SetFocus();
        tabPage.ResetFocus();

        Assert.IsFalse(tabPage.IsFocused);
    }

    #endregion

    #region Control Content Tests

    [TestMethod]
    public void TabPageCanHaveControls()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);
        var label = new Label("Test", mockThemeService.Object);

        tabPage.AddControl(label);

        Assert.AreEqual(1, tabPage.Controls.Count);
    }

    [TestMethod]
    public void TabPageCanHaveMultipleControls()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        for (int i = 0; i < 5; i++)
        {
            tabPage.AddControl(new Label($"Label{i}", mockThemeService.Object));
        }

        Assert.AreEqual(5, tabPage.Controls.Count);
    }

    [TestMethod]
    public void TabPageControlsCanBeRetrieved()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);
        var label1 = new Label("Test1", mockThemeService.Object);
        var label2 = new Label("Test2", mockThemeService.Object);

        tabPage.AddControl(label1);
        tabPage.AddControl(label2);

        Assert.AreEqual(label1, tabPage.Controls[0]);
        Assert.AreEqual(label2, tabPage.Controls[1]);
    }

    #endregion

    #region Inheritance Tests

    [TestMethod]
    public void TabPageInheritsFromControl()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        Assert.IsInstanceOfType(tabPage, typeof(Control));
    }

    [TestMethod]
    public void TabPageHasThemeService()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        Assert.IsNotNull(tabPage.ThemeService);
    }

    #endregion

    #region Color Priority Tests

    [TestMethod]
    public void TabPageColorPriorityEnabledAndFocusedWinsOverEnabled()
    {
        var mockThemeService = CreateMockThemeService();
        var colorScheme = new ColorScheme
        {
            Foreground = Color.White,
            Background = Color.Black,
            SelectedFocusedForeground = Color.Red,
            SelectedBackground = Color.Blue
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme);

        var tabPage = new TabPage("Tab1", mockThemeService.Object) { CanFocus = true };
        tabPage.SetFocus();

        tabPage.GetColor(out var foreground, out var background);

        Assert.AreEqual(Color.Red, foreground);
        Assert.AreEqual(Color.Blue, background);
    }

    [TestMethod]
    public void TabPageColorPriorityDisabledWinsOverEverything()
    {
        var mockThemeService = CreateMockThemeService();
        var colorScheme = new ColorScheme
        {
            Foreground = Color.White,
            Background = Color.Black,
            SelectedFocusedForeground = Color.Red,
            SelectedBackground = Color.Blue,
            DisabledForeground = Color.Gray,
            DisabledBackground = Color.DarkGray
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme);

        var tabPage = new TabPage("Tab1", mockThemeService.Object) 
        { 
            CanFocus = true, 
            IsEnabled = false 
        };
        tabPage.SetFocus();

        tabPage.GetColor(out var foreground, out var background);

        Assert.AreEqual(Color.Gray, foreground);
        Assert.AreEqual(Color.DarkGray, background);
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void TabPageCanHandleNullNameInConstructor()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage(null!, mockThemeService.Object);

        Assert.IsNull(tabPage.Name);
    }

    [TestMethod]
    public void TabPageCanHandleUnicodeInName()
    {
        var mockThemeService = CreateMockThemeService();
        var tabPage = new TabPage("Tab 📄 🔒 中文", mockThemeService.Object);

        Assert.AreEqual("Tab 📄 🔒 中文", tabPage.Name);
    }

    [TestMethod]
    public void TabPageGetColorMultipleCalls()
    {
        var mockThemeService = CreateMockThemeService();
        var colorScheme = new ColorScheme
        {
            Foreground = Color.White,
            Background = Color.Black
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme);

        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        for (int i = 0; i < 100; i++)
        {
            tabPage.GetColor(out var foreground, out var background);
            Assert.AreEqual(Color.White, foreground);
            Assert.AreEqual(Color.Black, background);
        }
    }

    [TestMethod]
    public void Draw_WithChildLabel_ChildIsRendered()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(20, 3));
        var tabPage = new TabPage("Tab1", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3)
        };
        var label = new Label("PageContent", mockThemeService.Object)
        {
            Position = new Position(0, 1),
            ViewSize = new Size(20, 1),
            Parent = tabPage
        };
        tabPage.AddControl(label);

        tabPage.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("PageContent"));
    }

    [TestMethod]
    public void Draw_EmptyTabPage_WritesNothing()
    {
        var mockThemeService = CreateMockThemeService();
        var buffer = new MockBuffer(new Size(20, 3));
        var tabPage = new TabPage("Tab1", mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3)
        };

        tabPage.Draw(buffer);

        Assert.AreEqual(string.Empty, buffer.ContentAsString);
    }

    [TestMethod]
    public void TabPageCanBeReusedAfterDisable()
    {
        var mockThemeService = CreateMockThemeService();
        var colorScheme = new ColorScheme
        {
            Foreground = Color.White,
            Background = Color.Black,
            DisabledForeground = Color.Gray,
            DisabledBackground = Color.DarkGray
        };
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(colorScheme);

        var tabPage = new TabPage("Tab1", mockThemeService.Object);

        tabPage.IsEnabled = false;
        tabPage.GetColor(out var foreground, out var background);
        Assert.AreEqual(Color.Gray, foreground);

        tabPage.IsEnabled = true;
        tabPage.GetColor(out foreground, out background);
        Assert.AreEqual(Color.White, foreground);
    }

    #endregion
}
