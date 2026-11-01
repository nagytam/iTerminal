using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;
using iConsole.Display;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class DialogTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void DialogConstructor()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var dialog = new Dialog("Test Dialog", mockThemeService.Object);
        Assert.IsNotNull(dialog);
        Assert.IsTrue(dialog.IsModal);
        Assert.IsFalse(dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DialogTitle()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var dialog = new Dialog("My Dialog", mockThemeService.Object);
        Assert.AreEqual("My Dialog", dialog.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DialogIsModal()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var dialog = new Dialog("Test", mockThemeService.Object);
        Assert.IsTrue(dialog.IsModal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DialogInitiallyNotVisible()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var dialog = new Dialog("Test", mockThemeService.Object);
        Assert.IsFalse(dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DialogInitialize()
    {
        // Note: Full initialization is complex and requires proper Control hierarchy setup
        // This test just verifies the Dialog can be created
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var dialog = new Dialog("Test", mockThemeService.Object);

        Assert.IsNotNull(dialog);
        Assert.AreEqual("Test", dialog.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DialogHasCommands()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var dialog = new Dialog("Test", mockThemeService.Object);
        Assert.IsTrue(dialog.Commands.Count > 0);
    }

    #region Escape and transient overlays

    /// <summary>
    /// Stands in for a control with something floating over the dialog — an autocompletion's
    /// suggestion list is the real case. A stub rather than the real thing on purpose: what is being
    /// pinned down here is the contract the dialog honours, not which control happens to implement it.
    /// </summary>
    private sealed class OverlayHostControl(IThemeService themeService)
        : Control(themeService), ITransientOverlayHost
    {
        public bool IsOverlayOpen { get; set; }
        public int CloseCount { get; private set; }

        public void CloseOverlay()
        {
            IsOverlayOpen = false;
            CloseCount++;
        }
    }

    private static Dialog CreateDialogWithOverlayHost(out OverlayHostControl host)
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var dialog = new Dialog("Test", mockThemeService.Object) { IsVisible = true };
        host = new OverlayHostControl(mockThemeService.Object);
        dialog.AddControl(host);
        return dialog;
    }

    private static KeyEvent Escape() =>
        new('\0', ConsoleKey.Escape, shift: false, alt: false, control: false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_WithNothingFloating_ClosesTheDialog()
    {
        var dialog = CreateDialogWithOverlayHost(out var host);

        var handled = await dialog.HandleKeyEventAsync(Escape());

        Assert.IsTrue(handled);
        Assert.IsFalse(dialog.IsVisible);
        Assert.AreEqual(0, host.CloseCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_WithSomethingFloating_TakesThatDownAndLeavesTheDialogOpen()
    {
        // The dialog claims Esc ahead of its children so nothing can strand the user in a dialog they
        // cannot dismiss — but a suggestion list up over the box being typed into is a sub-state the
        // key has to reach first, or it cannot be dismissed at all.
        var dialog = CreateDialogWithOverlayHost(out var host);
        host.IsOverlayOpen = true;

        var handled = await dialog.HandleKeyEventAsync(Escape());

        Assert.IsTrue(handled);
        Assert.AreEqual(1, host.CloseCount);
        Assert.IsTrue(dialog.IsVisible, "Dismissing what floats over a dialog is not closing the dialog.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_Twice_TakesTheOverlayThenTheDialog()
    {
        var dialog = CreateDialogWithOverlayHost(out var host);
        host.IsOverlayOpen = true;

        await dialog.HandleKeyEventAsync(Escape());
        await dialog.HandleKeyEventAsync(Escape());

        Assert.AreEqual(1, host.CloseCount);
        Assert.IsFalse(dialog.IsVisible, "One press per layer: the second Esc has nothing left to peel off.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_WithSomethingFloatingDeepInTheTree_StillReachesIt()
    {
        // The overlay's owner is rarely a direct child of the dialog — the real one is a text box on a
        // tab page — so the whole subtree is searched rather than the dialog's own child list.
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var dialog = new Dialog("Test", mockThemeService.Object) { IsVisible = true };
        var wrapper = new Control(mockThemeService.Object);
        var host = new OverlayHostControl(mockThemeService.Object) { IsOverlayOpen = true };
        wrapper.AddControl(host);
        dialog.AddControl(wrapper);

        var handled = await dialog.HandleKeyEventAsync(Escape());

        Assert.IsTrue(handled);
        Assert.AreEqual(1, host.CloseCount);
        Assert.IsTrue(dialog.IsVisible);
    }

    #endregion
}

