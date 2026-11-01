using iConsole.Controls;
using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

/// <summary>
/// The viewer and editor bars keep the width their dialog gave them. The base layout used to replace it with the
/// dialog's full width, which let items count as fitting that then stood on or past the dialog's border.
/// </summary>
[TestClass]
public class StatusBarWidthTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static ViewerStatusBar NewViewerBar() => new(
        new ScrollableModel(),
        new SearchModel(),
        new TextModel(),
        Mock.Of<IBeepService>(),
        Mock.Of<IDateTimeProviderService>(),
        Theme());

    private static EditorStatusBar NewEditorBar() => new(
        Mock.Of<IReadOnly>(),
        Mock.Of<IDirtyModel>(),
        Mock.Of<IDirtyReadOnlyModel>(),
        new ScrollableModel(),
        new SearchModel(),
        new TextModel(),
        Mock.Of<IContentModelService>(),
        Mock.Of<IBeepService>(),
        Mock.Of<IDateTimeProviderService>(),
        Theme());

    private static Window Dialog(uint width) => new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(width, 20) };

    private static long VisibleWidth(Control bar)
        => bar.Controls.Where(c => c.IsVisible).Sum(c => (long)c.ViewSize.Width);

    [Timeout(60_000)]
    [TestMethod]
    public void ViewerBar_KeepsTheWidthItsDialogGaveIt()
    {
        Window dialog = Dialog(100);
        ViewerStatusBar bar = NewViewerBar();
        dialog.AddControl(bar);
        bar.ViewSize = new Size(97, 1);
        bar.OnInitializeLayout();
        Assert.AreEqual(97L, bar.ViewSize.Width);
        Assert.AreEqual(1L, bar.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ViewerBar_NotYetSized_TakesTheDialogInterior()
    {
        Window dialog = Dialog(100);
        ViewerStatusBar bar = NewViewerBar();
        dialog.AddControl(bar);
        bar.OnInitializeLayout();
        Assert.AreEqual(98L, bar.ViewSize.Width);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ViewerBar_NeverShowsMoreThanItsWidth()
    {
        foreach (uint dialogWidth in new uint[] { 40, 60, 80, 100, 140 })
        {
            Window dialog = Dialog(dialogWidth);
            ViewerStatusBar bar = NewViewerBar();
            dialog.AddControl(bar);
            bar.ViewSize = new Size(dialogWidth - 3, 1);
            bar.OnInitializeLayout();
            Assert.IsTrue(VisibleWidth(bar) <= bar.ViewSize.Width, $"dialog {dialogWidth}: items {VisibleWidth(bar)} in {bar.ViewSize.Width}");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EditorBar_KeepsTheWidthItsDialogGaveIt_AndTakesTheInteriorWhenNotSized()
    {
        Window dialog = Dialog(100);
        EditorStatusBar sized = NewEditorBar();
        dialog.AddControl(sized);
        sized.ViewSize = new Size(98, 1);
        sized.OnInitializeLayout();
        Assert.AreEqual(98L, sized.ViewSize.Width);

        EditorStatusBar unsized = NewEditorBar();
        Dialog(100).AddControl(unsized);
        unsized.OnInitializeLayout();
        Assert.AreEqual(98L, unsized.ViewSize.Width);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EditorBar_NeverShowsMoreThanItsWidth()
    {
        foreach (uint dialogWidth in new uint[] { 40, 60, 80, 100, 140 })
        {
            Window dialog = Dialog(dialogWidth);
            EditorStatusBar bar = NewEditorBar();
            dialog.AddControl(bar);
            bar.ViewSize = new Size(dialogWidth - 2, 1);
            bar.OnInitializeLayout();
            Assert.IsTrue(VisibleWidth(bar) <= bar.ViewSize.Width, $"dialog {dialogWidth}: items {VisibleWidth(bar)} in {bar.ViewSize.Width}");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Bars_WithoutAParent_DoNotThrow()
    {
        NewViewerBar().OnInitializeLayout();
        NewEditorBar().OnInitializeLayout();
    }
}
