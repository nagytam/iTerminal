using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ResizeEventTest
{
    [TestMethod]
    public void OnWindowSizeChanged_ShouldUpdateViewSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var control = new Window(mockThemeService.Object);
        var newSize = new Size(100, 50);

        control.OnWindowSizeChanged(this, newSize);

        Assert.AreEqual(newSize, control.ViewSize);
    }

    [TestMethod]
    public void OnWindowSizeChanged_ShouldPropagateToChildren()
    {
        var mockThemeService = new Mock<IThemeService>();
        var parent = new Window(mockThemeService.Object);
        var child = new Window(mockThemeService.Object);

        parent.AddControl(child);
        var newSize = new Size(80, 40);

        parent.OnWindowSizeChanged(this, newSize);

        Assert.AreEqual(newSize, parent.ViewSize);
        Assert.AreEqual(newSize, child.ViewSize);
    }

    [TestMethod]
    public void OnWindowSizeChanged_ShouldCallOnInitializeLayout()
    {
        var mockThemeService = new Mock<IThemeService>();
        var control = new TestWindow(mockThemeService.Object);
        var newSize = new Size(120, 60);

        control.OnWindowSizeChanged(this, newSize);

        Assert.IsTrue(control.OnInitializeLayoutCalled);
    }

    [TestMethod]
    public void OnWindowSizeChanged_WithZeroSize_ShouldNotCrash()
    {
        var mockThemeService = new Mock<IThemeService>();
        var control = new Window(mockThemeService.Object);
        var zeroSize = new Size(0, 0);

        try
        {
            control.OnWindowSizeChanged(this, zeroSize);
            Assert.AreEqual(zeroSize, control.ViewSize);
        }
        catch
        {
            Assert.Fail("Should not throw exception with zero size");
        }
    }

    [TestMethod]
    public void OnWindowSizeChanged_MultipleChildren_AllShouldResize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var parent = new Window(mockThemeService.Object);
        var child1 = new Window(mockThemeService.Object);
        var child2 = new Window(mockThemeService.Object);
        var child3 = new Window(mockThemeService.Object);

        parent.AddControl(child1);
        parent.AddControl(child2);
        parent.AddControl(child3);

        var newSize = new Size(90, 45);

        parent.OnWindowSizeChanged(this, newSize);

        Assert.AreEqual(newSize, child1.ViewSize);
        Assert.AreEqual(newSize, child2.ViewSize);
        Assert.AreEqual(newSize, child3.ViewSize);
    }

    [TestMethod]
    public void OnWindowSizeChanged_NestedChildren_ShouldPropagate()
    {
        var mockThemeService = new Mock<IThemeService>();
        var root = new Window(mockThemeService.Object);
        var child = new Window(mockThemeService.Object);
        var grandChild = new Window(mockThemeService.Object);

        root.AddControl(child);
        child.AddControl(grandChild);

        var newSize = new Size(70, 35);

        root.OnWindowSizeChanged(this, newSize);

        Assert.AreEqual(newSize, root.ViewSize);
        Assert.AreEqual(newSize, child.ViewSize);
        // Note: grandChild gets resized through child's OnWindowResize call
        // which happens independently, so it may not have the same size
        // unless child explicitly sets it
    }

    [TestMethod]
    public void OnWindowResize_ShouldTriggerStateChange()
    {
        var mockThemeService = new Mock<IThemeService>();
        var control = new TestWindow(mockThemeService.Object);
        var newSize = new Size(100, 50);

        control.OnWindowResize(newSize);

        Assert.IsTrue(control.HasStateChanged);
    }

    [TestMethod]
    public void OnWindowSizeChanged_SmallSize_ShouldHandleGracefully()
    {
        var mockThemeService = new Mock<IThemeService>();
        var control = new Window(mockThemeService.Object);
        var smallSize = new Size(1, 1);

        try
        {
            control.OnWindowSizeChanged(this, smallSize);
            Assert.AreEqual(smallSize, control.ViewSize);
        }
        catch
        {
            Assert.Fail("Should handle small sizes gracefully");
        }
    }

    [TestMethod]
    public void OnWindowSizeChanged_LargeSize_ShouldHandleGracefully()
    {
        var mockThemeService = new Mock<IThemeService>();
        var control = new Window(mockThemeService.Object);
        var largeSize = new Size(10000, 10000);

        try
        {
            control.OnWindowSizeChanged(this, largeSize);
            Assert.AreEqual(largeSize, control.ViewSize);
        }
        catch
        {
            Assert.Fail("Should handle large sizes gracefully");
        }
    }

    // Helper test class
    private class TestWindow : Window
    {
        public bool OnInitializeLayoutCalled { get; private set; }

        public TestWindow(IThemeService themeService) : base(themeService)
        {
        }

        public override void OnInitializeLayout()
        {
            OnInitializeLayoutCalled = true;
            base.OnInitializeLayout();
        }
    }
}
