using iConsole.Controls;
using iConsole.Data;
using iConsole.Desktops;
using iConsole.Inputs;
using iConsole.Jobs;
using iConsole.Services;
using iConsole.Test.Core.Automation;
using Moq;

namespace iConsole.Test.Inputs;

[TestClass]
public class HoverAndInjectorTest
{
    private static MouseEvent DecodeSingle(string sequence)
    {
        var decoder = new TerminalInputUtf8Decoder();
        MouseEvent? mouse = null;
        decoder.Append(System.Text.Encoding.ASCII.GetBytes(sequence));
        decoder.Drain(_ => { }, m => mouse = m);
        Assert.IsNotNull(mouse);
        return mouse!;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Motion_WithNoButtonHeld_IsDecodedAsHover()
    {
        // Button code 35 is motion (32) with no button (3).
        var mouse = DecodeSingle("\x1b[<35;10;5M");

        Assert.IsTrue(mouse.IsHover);
        Assert.IsFalse(mouse.IsDrag);
        Assert.IsFalse(mouse.LeftClicked);
        Assert.AreEqual(9, mouse.Position.X);
        Assert.AreEqual(4, mouse.Position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Motion_WithLeftButtonHeld_IsADragNotAHover()
    {
        var mouse = DecodeSingle("\x1b[<32;10;5M");

        Assert.IsTrue(mouse.IsDrag);
        Assert.IsFalse(mouse.IsHover);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Click_IsNotAHover()
    {
        var mouse = DecodeSingle("\x1b[<0;10;5M");

        Assert.IsTrue(mouse.LeftClicked);
        Assert.IsFalse(mouse.IsHover);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Preferred_Reporting_EnablesAnyMotion()
    {
        StringAssert.Contains(TerminalMouseAnsi.EnablePreferredMouseReporting, "\x1b[?1003h");
    }

    /// <summary>
    /// The injector goes through the same consumer as a real keyboard: a key reaches the modal control and reports whether it
    /// was handled, and a hover moves the tooltip service's pointer without a click.
    /// </summary>
    [TestMethod]
    [Timeout(10_000)]
    public async Task Injector_RoutesKeysAndHoversThroughTheConsumer()
    {
        var input = new VirtualTerminalInput();
        IInputInjector injector = input;
        var tips = new ToolTipService();

        Mock<IControl> modal = new();
        modal.Setup(c => c.HandleKeyEventAsync(It.IsAny<KeyEvent>())).ReturnsAsync(true);
        modal.Setup(c => c.HandleMouseEventAsync(It.IsAny<MouseEvent>())).ReturnsAsync(true);
        Mock<IDesktop> desktop = new();
        desktop.Setup(d => d.GetModalChildControl()).Returns(modal.Object);

        var job = new VirtualTerminalInputJob(input, new Mock<IBeepService>().Object, tips) { Desktop = desktop.Object };
        using var cancellation = new CancellationTokenSource();
        var running = job.ExecuteAsync(cancellation.Token);

        var handled = await injector.SendKeyAsync(new KeyEvent('a', ConsoleKey.A, false, false, false));
        Assert.IsTrue(handled);

        var hover = new MouseEvent { Position = new Position(7, 3), IsHover = true };
        await injector.SendMouseAsync(hover);
        Assert.AreEqual(7, tips.PointerPosition!.X);
        Assert.AreEqual(3, tips.PointerPosition.Y);

        await injector.SendMouseAsync(new MouseEvent { Position = new Position(7, 3), LeftClicked = true });
        Assert.IsNull(tips.PointerPosition, "A click dismisses the tooltip.");

        cancellation.Cancel();
        await running;
    }
}
