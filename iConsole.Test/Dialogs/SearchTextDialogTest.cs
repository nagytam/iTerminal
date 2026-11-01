using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Dialogs;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Dialogs.Test;

[TestClass]
public class SearchTextDialogTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void SearchValue_ShouldReflectTextBoxText()
    {
        var searchModel = new Mock<ISearchModel>();
        var textModel = new Mock<ITextModel>();
        var scrollable = new Mock<iConsole.Model.IScrollableModel>();
        var beep = new Mock<IBeepService>();
        var dt = new Mock<IDateTimeProviderService>();
        var theme = new Mock<IThemeService>();
        theme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var dlg = new SearchTextDialog(searchModel.Object, textModel.Object, scrollable.Object, beep.Object, dt.Object, theme.Object);
        dlg.OnInitializeLayout();

        // set internal textbox text via reflection
        var field = dlg.GetType().GetField("_textBoxSearch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var textBox = (iConsole.Controls.TextBox)field!.GetValue(dlg)!;
        textBox.Text = "needle";

        Assert.AreEqual("needle", dlg.SearchValue);
    }
}

